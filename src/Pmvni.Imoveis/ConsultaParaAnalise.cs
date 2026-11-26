using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Imoveis;

/// <summary>Consulta local das chaves extraídas das peças de um processo. A saída não inclui nomes ou documentos de terceiros.</summary>
public static class ConsultaParaAnalise
{
    public const string Comando = "--consultar-qgis-analise";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static int Executar(string[] args, TextWriter erros)
    {
        if (args.Length != 4 || args[0] != Comando)
        {
            erros.WriteLine("Uso: --consultar-qgis-analise <pasta QGIS> <entrada JSON> <resultado JSON novo>");
            return 2;
        }

        try
        {
            var resultado = ConsultarArquivo(args[1], null, args[2], CancellationToken.None);
            using var saida = new FileStream(args[3], FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(saida, resultado, Json);
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            erros.WriteLine("Consulta territorial indisponível: " + e.GetType().Name + ".");
            return 1;
        }
    }

    public static JsonObject ConsultarArquivo(string? configurada, string? doKit, string arquivo, CancellationToken ct)
    {
        var entrada = JsonSerializer.Deserialize<Entrada>(File.ReadAllText(arquivo), Json)
            ?? throw new InvalidDataException("Falta a entrada da consulta QGIS.");
        var tentativas = new List<object>();
        foreach (var pasta in new[] { configurada, doKit }.OfType<string>().Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var baseQgis = BaseQgis.Carregar(pasta, ct: ct);
                if (baseQgis[Camada.Lotes] is null)
                    throw new InvalidDataException("A camada Lotes não abriu.");
                var resultado = JsonSerializer.SerializeToNode(Consultar(baseQgis, entrada, ct), Json)!.AsObject();
                resultado["pasta"] = pasta;
                resultado["tentativas_anteriores"] = JsonSerializer.SerializeToNode(tentativas, Json);
                return resultado;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                tentativas.Add(new { pasta, motivo = e.GetType().Name });
            }
        }
        return JsonSerializer.SerializeToNode(new
        {
            situacao = "INDISPONIVEL",
            motivo = tentativas.Count == 0 ? "Nenhuma pasta QGIS disponível." : "As fontes tentadas não forneceram cadastro e camada Lotes utilizáveis.",
            tentativas
        }, Json)!.AsObject();
    }

    static object Consultar(BaseQgis baseQgis, Entrada entrada, CancellationToken ct)
    {
        var pessoas = entrada.Pessoas ?? [];
        var enderecos = (entrada.Enderecos ?? []).Concat((entrada.Bcis ?? []).Select(b => b.Endereco).OfType<Endereco>())
            .Where(e => !string.IsNullOrWhiteSpace(e.Logradouro)).ToArray();
        var inscricoes = (entrada.Inscricoes ?? []).Concat((entrada.Bcis ?? []).Select(b => b.Inscricao))
            .Select(Inscricoes.Normalizar).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var reduzidas = (entrada.Bcis ?? []).Select(b => Digitos(b.InscricaoReduzida)).Where(n => n.Length > 0).ToHashSet();
        var area = entrada.AreaTerreno ?? (entrada.Bcis ?? []).Select(b => b.AreaTerreno).FirstOrDefault(a => a > 0);
        var ruas = baseQgis[Camada.Logradouros]?.Feicoes.Where(r => enderecos.Any(e => Tem(r.Atributo("NomeLogra"), e.Logradouro))).ToArray() ?? [];
        var grupos = new Dictionary<int, Acerto>();
        int nome = 0, documento = 0, nomeEDocumento = 0, endereco = 0, inscricao = 0, bci = 0;

        foreach (var registro in baseQgis.Cadastro.Registros.Where(r => !TextosConsulta.Baixada(r)))
        {
            ct.ThrowIfCancellationRequested();
            var nomeConfere = pessoas.Any(p => Igual(registro.Contribuinte, p.Nome));
            var doc = Digitos(registro.Campo("n_documento"));
            var idConfere = doc.Length is 11 or 14 && pessoas.Any(p => Digitos(p.Documento) == doc);
            bool MesmoEndereco(Endereco e) => Tem(registro.Campo("logradouro"), e.Logradouro) &&
                (string.IsNullOrWhiteSpace(e.Numero) || Igual(registro.Campo("n_metrico"), e.Numero));
            var local = enderecos.Any(MesmoEndereco);
            var bairro = enderecos.Any(e => MesmoEndereco(e) && Igual(registro.Bairro, e.Bairro));
            var chave = Inscricoes.Normalizar(registro.InscricaoMunicipal);
            var chaveConfere = chave is not null && inscricoes.Contains(chave);
            var bciConfere = reduzidas.Contains(Digitos(registro.Campo("inscricao_reduzida")));
            if (nomeConfere) nome++;
            if (idConfere) documento++;
            if (nomeConfere && idConfere && pessoas.Any(p => Igual(registro.Contribuinte, p.Nome) && Digitos(p.Documento) == doc))
                nomeEDocumento++;
            if (local) endereco++;
            if (chaveConfere) inscricao++;
            if (bciConfere) bci++;
            if (!(nomeConfere || idConfere || local || chaveConfere || bciConfere)) continue;

            foreach (var lote in baseQgis.Vincular(registro).Lotes)
            {
                var a = Obter(lote);
                a.Nome |= nomeConfere;
                a.Documento |= idConfere;
                a.Endereco |= local;
                a.Bairro |= bairro;
                a.Bci |= bciConfere;
            }
        }

        foreach (var lote in baseQgis[Camada.Lotes]?.Feicoes ?? [])
        {
            ct.ThrowIfCancellationRequested();
            var chave = Inscricoes.Normalizar(lote.Atributo("InscImob"));
            if (chave is not null && inscricoes.Contains(chave)) Obter(lote).Inscricao = true;
            if (area is > 0 && baseQgis[Camada.Lotes]?.Geografica == false && baseQgis[Camada.Logradouros]?.Geografica == false &&
                lote.Geometria is { } g && Math.Abs(g.Area - area.Value) / area.Value <= .05 &&
                ruas.Any(r => r.Geometria?.IsWithinDistance(g, 40) == true))
                Obter(lote).Geometria = true;
        }

        var selecionados = grupos.Values.Where(a => a.Inscricao || a.Bci || a.Geometria || a.Endereco && (a.Nome || a.Documento))
            .OrderByDescending(a => a.Inscricao).ThenByDescending(a => a.Bci).ThenByDescending(a => a.Documento)
            .ThenByDescending(a => a.Nome && a.Endereco).ThenByDescending(a => a.Geometria).ThenBy(a => a.Lote.Indice);
        return new
        {
            situacao = "CONSULTADA",
            fonte = "Cadastro imobiliário municipal e camada Lotes",
            camadas = new { lotes = true, logradouros = baseQgis[Camada.Logradouros] is not null, zoneamento = baseQgis[Camada.Zoneamento] is not null },
            buscas = new { nomes = nome, identificadores = documento, nomeEDocumento, enderecos = endereco, inscricoes = inscricao, bcisPorCodigoReduzido = bci, trechosDaVia = ruas.Length },
            candidatos = selecionados.Take(25).Select(a => new
            {
                feicao = a.Lote.Indice, inscricao = a.Lote.Atributo("InscImob"),
                area = a.Lote.Geometria is { } g ? Math.Round(g.Area, 2) : (double?)null,
                distanciaDaVia = a.Lote.Geometria is { } pol && ruas.Length > 0
                    ? Math.Round(ruas.Min(r => r.Geometria!.Distance(pol)), 2) : (double?)null,
                chaves = new { a.Inscricao, a.Bci, a.Nome, a.Documento, a.Endereco, a.Bairro, a.Geometria }
            })
        };

        Acerto Obter(Feicao lote)
        {
            if (!grupos.TryGetValue(lote.Indice, out var encontrado))
                grupos.Add(lote.Indice, encontrado = new Acerto(lote));
            return encontrado;
        }
    }

    static bool Igual(string? dado, string? valor) =>
        !string.IsNullOrWhiteSpace(valor) && Pesquisa.Corresponde(dado, Condicao.Igual, valor);
    static bool Tem(string? dado, string? valor) =>
        !string.IsNullOrWhiteSpace(valor) && Pesquisa.Corresponde(dado, Condicao.Contem, valor);
    static string Digitos(string? valor) => new((valor ?? "").Where(char.IsAsciiDigit).ToArray());

    sealed record Entrada(Pessoa[]? Pessoas, Endereco[]? Enderecos, string[]? Inscricoes, Bci[]? Bcis, double? AreaTerreno);
    sealed record Pessoa(string? Nome, string? Documento);
    sealed record Endereco(string? Logradouro, string? Numero, string? Bairro);
    sealed record Bci(string? Inscricao, string? InscricaoReduzida, double? AreaTerreno, Endereco? Endereco);
    sealed class Acerto(Feicao lote)
    {
        public Feicao Lote { get; } = lote;
        public bool Inscricao, Bci, Nome, Documento, Endereco, Bairro, Geometria;
    }
}
