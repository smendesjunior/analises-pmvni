using System.Security.Cryptography;
using System.Text.Json;

namespace Pmvni.App;

/// <summary>Manifesto publicado na Release do GitHub junto do executável (<see cref="RegrasAtualizacao.NomeManifesto"/>).</summary>
/// <param name="Notas">O que mudou nesta versão (texto simples, uma linha por item); opcional.</param>
public sealed record ManifestoAtualizacao(string? Versao, string Compilacao, string Sha256, long Tamanho, string? Url, string? Notas);

/// <summary>
/// Regras da atualização automática do editor (no padrão do <c>UpdateRules</c> do WinStatusBar): onde procurar, o que aceitar, como conferir
/// o download e como trocar o executável. O editor é um executável único e portátil (sem instalador): a versão nova é baixada, conferida
/// (tamanho e SHA-256) e trocada no lugar do .exe em uso, que é só renomeado (o Windows deixa renomear um .exe aberto) e apagado na
/// próxima abertura. Vale ao abrir o editor de novo. Sem tipos WinUI: compilado também nos testes.
/// </summary>
public static class RegrasAtualizacao
{
    public const string Repositorio = "smendesjunior/analises-pmvni";
    public const string NomeManifesto = "EditorRelatoriosPMVNI-update.json";
    public const string NomeExecutavelPublicado = "EditorRelatoriosPMVNI.exe";

    /// <summary>Link fixo da Release mais recente (sem token e sem o limite de consultas da API do GitHub).</summary>
    public const string UrlManifesto = "https://github.com/" + Repositorio + "/releases/latest/download/" + NomeManifesto;

    /// <summary>Só executáveis das Releases deste repositório são aceitos.</summary>
    public const string PrefixoDownload = "https://github.com/" + Repositorio + "/releases/download/";

    /// <summary>Prefixo dos executáveis antigos renomeados durante a troca (apagados na próxima abertura).</summary>
    public const string PrefixoAntigo = "antigo-";
    public const string SufixoAntigo = ".exe.old";

    public static readonly TimeSpan PrimeiraConsulta = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(15);

    /// <summary>Espera da consulta automática: 15 min; depois de 3 falhas seguidas, 6 h.</summary>
    public static TimeSpan ProximaConsulta(int falhasSeguidas) => falhasSeguidas >= 3 ? TimeSpan.FromHours(6) : Intervalo;

    /// <summary>Carimbo de compilação válido: 14 dígitos (aaaaMMddHHmmss).</summary>
    public static bool CompilacaoValida(string? c) => c is { Length: 14 } && c.All(char.IsAsciiDigit);

    /// <summary>Lê o manifesto; null se não for um manifesto (HTML de erro, JSON quebrado, campos com tipo errado).</summary>
    public static ManifestoAtualizacao? Ler(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json.TrimStart('﻿'));
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            string? build = Texto(r, "Build"), sha = Texto(r, "Sha256");
            if (build is null || sha is null || !r.TryGetProperty("Size", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var tamanho)) return null;
            return new ManifestoAtualizacao(Texto(r, "Version"), build, sha, tamanho, Texto(r, "Url"), Texto(r, "Notes"));
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException) { return null; }
    }

    static string? Texto(JsonElement r, string nome) =>
        r.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>A publicação é mais nova que esta compilação.</summary>
    public static bool MaisNova(ManifestoAtualizacao m, string compilacaoAtual) =>
        CompilacaoValida(m.Compilacao) && string.CompareOrdinal(m.Compilacao, compilacaoAtual) > 0;

    /// <summary>Mais nova, tamanho e hash bem formados e executável das Releases deste repositório.</summary>
    public static bool Aceitavel(ManifestoAtualizacao m, string compilacaoAtual) =>
        MaisNova(m, compilacaoAtual)
        && m.Tamanho is > 1_000_000 and < 2_000_000_000
        && m.Sha256 is { Length: 64 } && m.Sha256.All(char.IsAsciiHexDigit)
        && m.Url is not null && m.Url.StartsWith(PrefixoDownload, StringComparison.Ordinal) && !m.Url.Contains("..", StringComparison.Ordinal);

    /// <summary>Confere tamanho, SHA-256 e cabeçalho de executável (MZ) do arquivo baixado antes de qualquer troca.</summary>
    public static void Conferir(string arquivo, ManifestoAtualizacao m)
    {
        if (new FileInfo(arquivo).Length != m.Tamanho)
            throw new InvalidDataException("O download veio incompleto; a atualização fica para a próxima consulta.");
        using var f = File.OpenRead(arquivo);
        var cabecalho = new byte[2];
        if (f.Read(cabecalho, 0, 2) != 2 || cabecalho[0] != 0x4D || cabecalho[1] != 0x5A)
            throw new InvalidDataException("O arquivo baixado não é um executável; a atualização foi ignorada.");
        f.Position = 0;
        if (!Convert.ToHexString(SHA256.HashData(f)).Equals(m.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A cópia baixada não confere com a publicação; a atualização fica para a próxima consulta.");
    }

    /// <summary>Download de uma tentativa anterior que ainda confere: dispensa baixar de novo.</summary>
    public static bool PodeReaproveitar(string arquivo, ManifestoAtualizacao m)
    {
        if (!File.Exists(arquivo)) return false;
        try { Conferir(arquivo, m); return true; }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Troca o executável em uso pelo novo, já conferido: o atual é renomeado para <c>antigo-&lt;guid&gt;.exe.old</c> na mesma pasta (o Windows
    /// deixa renomear um .exe aberto) e o novo é copiado com nome temporário, conferido e movido para o nome do atual. Se algo falha depois
    /// de renomear, o atual volta ao nome original: a pasta nunca fica sem o executável.
    /// </summary>
    public static void TrocarExecutavel(string exeAtual, string exeNovo, ManifestoAtualizacao m)
    {
        var pasta = Path.GetDirectoryName(exeAtual) ?? throw new InvalidOperationException("Pasta do executável desconhecida.");
        var temporario = Path.Combine(pasta, "novo-" + Guid.NewGuid().ToString("N") + ".exe.tmp");
        var antigo = Path.Combine(pasta, PrefixoAntigo + Guid.NewGuid().ToString("N") + SufixoAntigo);
        File.Copy(exeNovo, temporario, overwrite: false);
        try
        {
            Conferir(temporario, m);
            File.Move(exeAtual, antigo);
            try { File.Move(temporario, exeAtual); }
            catch
            {
                File.Move(antigo, exeAtual);
                throw;
            }
        }
        finally
        {
            try { if (File.Exists(temporario)) File.Delete(temporario); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        try { File.Delete(antigo); } catch (IOException) { } catch (UnauthorizedAccessException) { } // em uso: sai na próxima abertura
    }

    /// <summary>Apaga os executáveis antigos deixados por trocas anteriores (os que ainda estão em uso ficam para a próxima vez).</summary>
    public static int LimparAntigos(string pasta)
    {
        int apagados = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(pasta, PrefixoAntigo + "*" + SufixoAntigo))
                try { File.Delete(f); apagados++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return apagados;
    }

    /// <summary>"2.0.5 · compilação 20261116021306" (ou "compilação de desenvolvimento").</summary>
    public static string TextoVersao(string versao, string? compilacao) =>
        versao + " · " + (CompilacaoValida(compilacao) ? "compilação " + compilacao : "compilação de desenvolvimento");
}
