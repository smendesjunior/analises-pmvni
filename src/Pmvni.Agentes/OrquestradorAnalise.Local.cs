using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Agentes;

public sealed partial class OrquestradorAnalise
{
    internal const string ComandoCheckpoint = "--salvar-retomada-local";
    internal string? DocumentosParaTeste { get; init; }

    internal static string RegistroDoTrabalho(string pasta, string registro) =>
        Path.Combine(pasta, ".pmvni-ai", "editor", Path.GetFileName(registro));

    internal static string ProcessoDoTrabalho(string origem, string registro) =>
        AreaAnaliseLocal.LocalExistente(origem, registro) ?? origem;

    // Entrada sem janela, usada também pelo controlador destacado depois de o Editor fechar.
    internal static int SalvarRetomada(string[] args, TextWriter erro)
    {
        if (args.Length != 5 || args[4] is not ("analise" or "analise-pro" or "analise-google")) return 2;
        try
        {
            var origem = Path.GetFullPath(args[1]);
            if (Path.GetFileName(args[3]) != args[3] || args[3] is "" or "." or "..") return 2;
            // File.Exists perderia o código de erro de um compartilhamento desconectado.
            _ = File.GetAttributes(origem);
            var registro = RegistroDoTrabalho(origem, args[3]);
            var local = AreaAnaliseLocal.LocalExistente(origem, registro);
            if (RegistroExecucao.MotivoInvalido(origem, registro) is not null || local is null
                || !Path.GetFullPath(args[2]).Equals(local, StringComparison.OrdinalIgnoreCase)) return 2;
            RecuperacaoAnaliseLocal.Salvar(origem, local, args[3], args[4]);
            return 0;
        }
        catch (IOException ex) when (ServidorIndisponivel(ex))
        {
            erro.WriteLine("O servidor do checkpoint está indisponível. " + ex.Message);
            return 3;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException or KeyNotFoundException or System.Security.Cryptography.CryptographicException)
        {
            erro.WriteLine(ex.Message);
            return 1;
        }
    }

    static bool ServidorIndisponivel(IOException ex) =>
        (ex.HResult & 0xffff) is 53 or 59 or 64 or 67 or 121 or 1203 or 1231 or 1232 or 2250;

    async Task<FimAnalise> RodarEmDocumentosAsync(PedidoExecucao pedido, Func<PedidoExecucao, Task<FimAnalise>> executar,
        DateTimeOffset inicio, IProgress<ProgressoAnalise> progresso, CancellationToken ct, bool acompanhamento = false)
    {
        var origem = pedido.Processo.Pasta;
        var id = Path.GetFileName(pedido.Registro.Pasta);
        var rota = Path.GetFileName(ResultadoAnalise.PastaControle(pedido.Provedor));
        var controlador = pedido.Provedor == Provedor.ChatGpt ? pedido.Pecas.Analise62 : pedido.Pecas.RuntimeDoKit(pedido.Provedor);
        var transportador = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(controlador!)!, "..", "..", "portabilidade_execucao.py"));
        if (!File.Exists(transportador))
            return Falha("Atualize o kit PMVNI para a versão 4.6.0 ou posterior para trabalhar na área local da análise.");
        Anunciar(progresso, new("Conferindo os documentos do processo e preparando as cópias locais.", null));
        var area = await Task.Run(() => acompanhamento
            ? AreaAnaliseLocal.AbrirAtiva(origem, pedido.Registro.Pasta, DocumentosParaTeste)
            : AreaAnaliseLocal.Preparar(pedido, EntradasDoProcesso(pedido, ct), ct, DocumentosParaTeste), ct).ConfigureAwait(false);
        if (area.LocalAnterior is { } anterior)
        {
            List<string> args = [];
            if (pedido.Pecas.PythonPeloLauncher) args.Add("-3");
            args.AddRange(["-B", "-X", "utf8", transportador, "--raiz-anterior", anterior, "--case", area.ProcessoLocal, "--execucao", id, "--controle", area.ControleAnterior ?? rota]);
            var validacao = await ExecucaoProcesso.RodarAsync(pedido.Pecas.Python!, args, area.ProcessoLocal,
                Ambiente(pedido.Provedor, ModoAcesso.Conta, null), TimeSpan.FromMinutes(20), ct).ConfigureAwait(false);
            if (validacao.Codigo != 0)
                return Falha("O kit não confirmou a etapa salva na cópia local. A retomada foi preservada. " + validacao.Saida.Trim());
            area.ConfirmarTransporte();
        }
        pedido.AoPrepararLocal?.Invoke(area.ProcessoLocal);
        Anunciar(progresso, new("Cópias conferidas. Pasta de trabalho: " + area.ProcessoLocal, null));
        using var registro = RegistroExecucao.Abrir(area.RegistroLocal, area.ProcessoLocal);
        if (pedido.Retomar && !acompanhamento)
            SincronizarRecusa(pedido, area, registro);
        var trabalho = pedido with { Processo = pedido.Processo with { Pasta = area.ProcessoLocal }, Registro = registro, OrigemCompartilhada = origem, Retomar = pedido.Retomar && !area.InicioPendente };
        pedido.Registro.Atualizar(o =>
        {
            o["area_local_versao"] = 1;
            o["area_local_id"] = area.Identificador;
            o["processo"] = origem;
            o["inicio"] ??= inicio.ToString("o");
            o["usuario"] = Usuario();
            o["estacao"] = Environment.MachineName;
            o["situacao"] = "em_andamento";
            o["provedor"] = Provedores.Sufixo(pedido.Provedor);
        });
        FimAnalise fim;
        try
        {
            // A reabertura após perda da rede pode encontrar a entrega já validada e ainda sem publicação.
            var pronto = pedido.Retomar && File.Exists(Path.Combine(registro.Pasta, "PUBLICACAO-PENDENTE-" + rota + ".json"))
                && !File.Exists(Path.Combine(registro.Pasta, RegistroExecucao.NomeRecusaEntrega));
            if (pronto)
            {
                var produto = ResultadoAnalise.Localizar(area.ProcessoLocal, pedido.Provedor, RegistroExecucao.LerInicio(registro.Pasta)?.UtcDateTime ?? inicio.UtcDateTime, registro.Pasta);
                if (produto.Documento is null) throw new IOException(produto.Motivo ?? "Entrega local indisponível.");
                fim = new("concluida", produto.Documento, produto.Pdf, null, 0);
            }
            else fim = await executar(trabalho).ConfigureAwait(false);
            if (fim.Situacao == "concluida" && fim.Documento is not null)
            {
                var marca = Path.Combine(registro.Pasta, "PUBLICACAO-PENDENTE-" + rota + ".json");
                File.WriteAllText(marca, "{}");
                FimAnalise publicado;
                try
                {
                    // Preservar o erro nativo caso o compartilhamento tenha caído entre a análise e a entrega.
                    _ = File.GetAttributes(origem);
                    RegistrarCheckpoint();
                    publicado = RecuperacaoAnaliseLocal.Publicar(origem, area.ProcessoLocal, id, rota, fim, () => area.ConferirOriginais(ct), ct);
                }
                catch (IOException ex) when (ServidorIndisponivel(ex))
                {
                    var pendente = new FimAnalise("interrompida", null, null,
                        "Relatório validado e salvo na estação. A publicação será repetida na retomada quando o servidor estiver disponível.", null);
                    Encerrar(trabalho, pendente, inicio);
                    return pendente;
                }
                File.Delete(marca);
                Encerrar(trabalho, fim with { Situacao = publicado.Situacao, Mensagem = publicado.Mensagem }, inicio);
                return publicado;
            }
            if (!fim.AcompanhamentoPendente && fim.Situacao != AcompanhamentoInterrompido) RegistrarCheckpoint();
            Encerrar(trabalho, fim, inicio);
            return fim;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (pedido.Provedor == Provedor.ChatGpt) RegistrarCheckpoint();
            throw;
        }
        finally
        {
            registro.Descarregar();
            try { CopiarResumoDoRegistro(registro, pedido.Registro); }
            catch (IOException ex) when (ServidorIndisponivel(ex))
            {
                Anunciar(progresso, new("Resumo preservado na estação enquanto o servidor está indisponível.", null));
            }
        }

        void RegistrarCheckpoint()
        {
            var controle = Path.Combine(area.ProcessoLocal, ResultadoAnalise.PastaControle(pedido.Provedor));
            if (File.Exists(Path.Combine(controle, "engine_state.json")) || File.Exists(Path.Combine(registro.Pasta, "continuidade.json")))
                RecuperacaoAnaliseLocal.Salvar(origem, area.ProcessoLocal, id, rota);
        }
    }

    // A importação pode ter sido recusada em outro computador depois do último checkpoint.
    static void SincronizarRecusa(PedidoExecucao pedido, AreaAnaliseLocal area, RegistroExecucao registro)
    {
        var marcador = Path.Combine(pedido.Registro.Pasta, RegistroExecucao.NomeRecusaEntrega);
        if (!File.Exists(marcador)) return;
        var recusa = JsonNode.Parse(File.ReadAllText(marcador));
        var id = Path.GetFileName(registro.Pasta);
        var rota = pedido.Provedor switch { Provedor.ChatGpt => "codex", Provedor.Claude => "claude", _ => "gemini" };
        if (recusa?["execution"]?.GetValue<string>() != id || recusa["rota"]?.GetValue<string>() != rota) return;
        var arquivo = ResultadoAnalise.Localizar(area.ProcessoLocal, pedido.Provedor, DateTime.MinValue, registro.Pasta).Documento;
        if (arquivo is null) return;
        var sha = recusa["sha256"]?.GetValue<string>();
        using var fs = File.OpenRead(arquivo);
        if (sha != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(fs))) return;
        if (recusa["erros"] is not JsonArray erros || erros.Count == 0) throw new InvalidDataException("A recusa da entrega não informa o motivo.");
        registro.RecusarEntrega(pedido.Provedor, arquivo, sha, erros.Select(e => e!.GetValue<string>()).ToArray());
    }

    static void CopiarResumoDoRegistro(RegistroExecucao local, RegistroExecucao origem)
    {
        if (!File.Exists(Path.Combine(local.Pasta, "execucao.json"))) return;
        var dados = JsonNode.Parse(RegistroExecucao.LerTextoDaExecucao(local.Pasta)) as JsonObject;
        if (dados is null) return;
        origem.Atualizar(o =>
        {
            foreach (var chave in new[] { "provedor_origem", "provedor", "modo", "inicio", "retomadas", "versao_do_editor", "versao_do_plugin",
                "plugin", "usuario", "perfil", "autenticacao", "rota_codex_tecnica", "rota_codex_origem", "retomada", "decisoes",
                FluxoLinear.Chave, TabelaDeModelos.ChaveNoRegistro })
                if (dados.ContainsKey(chave)) o[chave] = dados[chave]?.DeepClone();
        });
    }
}
