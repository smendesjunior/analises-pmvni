namespace Pmvni.Imoveis;

/// <summary>Um ponto na tela do mapa (pixels do viewBox, y para baixo).</summary>
public readonly record struct PontoTela(double X, double Y);

/// <summary>Um retângulo alinhado aos eixos, na tela.</summary>
public readonly record struct Caixa(double X0, double Y0, double X1, double Y1)
{
    public double Largura => X1 - X0;
    public double Altura => Y1 - Y0;
    public PontoTela Centro => new((X0 + X1) / 2, (Y0 + Y1) / 2);
    public bool Intersecta(Caixa o) => X0 < o.X1 && o.X0 < X1 && Y0 < o.Y1 && o.Y0 < Y1;
    public double AreaComum(Caixa o) => Math.Max(0, Math.Min(X1, o.X1) - Math.Max(X0, o.X0)) * Math.Max(0, Math.Min(Y1, o.Y1) - Math.Max(Y0, o.Y0));
    public Caixa Expandida(double m) => new(X0 - m, Y0 - m, X1 + m, Y1 + m);
    public PontoTela[] Contorno() => [new(X0, Y0), new(X1, Y0), new(X1, Y1), new(X0, Y1)];
}

/// <summary>Uma posição possível de um rótulo: centro, tamanho da caixa, giro (graus, anti-horário) e o texto a escrever nela (pode ser
/// uma versão menor ou abreviada).</summary>
public sealed record CandidatoRotulo(PontoTela Centro, double Largura, double Altura, double Angulo = 0, string? Texto = null)
{
    public PontoTela[] Contorno() => Rotulagem.CaixaGirada(Centro, Largura, Altura, Angulo);
}

/// <summary>Um rótulo a pôr: id, prioridade (menor = mais importante) e as posições em ordem de preferência.</summary>
public sealed record PedidoRotulo(string Id, int Prioridade, IReadOnlyList<CandidatoRotulo> Candidatos);

/// <summary>Um rótulo posto: qual candidato coube e o contorno ocupado. Pedido sem candidato que caiba não aparece (fica oculto).</summary>
public sealed record RotuloPosto(string Id, int Prioridade, int Indice, CandidatoRotulo Candidato, PontoTela[] Contorno);

/// <summary>De que lado do lote o balão ficou.</summary>
public enum LadoBalao { Abaixo, Acima, Direita, Esquerda, Livre }

/// <summary>A ocupação da tela: os contornos já postos, com uma grade espacial (células de <c>celula</c> px) para o teste de sobreposição
/// ficar rápido com centenas de rótulos. Um contorno só entra se couber inteiro na tela (com a margem) e não cobrir nenhum outro.</summary>
public sealed class QuadroRotulos
{
    readonly double largura, altura, margem, celula;
    readonly int colunas, linhas;
    readonly List<(PontoTela[] Poli, Caixa Env)> postos = [];
    readonly List<int>[] grade;

    public QuadroRotulos(double largura, double altura, double margem = 2, double celula = 64)
    {
        if (largura <= 0 || altura <= 0 || celula <= 0) throw new ArgumentOutOfRangeException(nameof(largura));
        (this.largura, this.altura, this.margem, this.celula) = (largura, altura, margem, celula);
        colunas = Math.Max(1, (int)Math.Ceiling(largura / celula));
        linhas = Math.Max(1, (int)Math.Ceiling(altura / celula));
        grade = new List<int>[colunas * linhas];
    }

    public double Largura => largura;
    public double Altura => altura;
    public IReadOnlyList<PontoTela[]> Ocupados => postos.Select(p => p.Poli).ToList();

    public bool DentroDaTela(PontoTela[] p) => p.All(v => margem <= v.X && v.X <= largura - margem && margem <= v.Y && v.Y <= altura - margem);

    IEnumerable<int> Celulas(Caixa e)
    {
        int c0 = Math.Clamp((int)Math.Floor(e.X0 / celula), 0, colunas - 1), c1 = Math.Clamp((int)Math.Floor(e.X1 / celula), 0, colunas - 1);
        int l0 = Math.Clamp((int)Math.Floor(e.Y0 / celula), 0, linhas - 1), l1 = Math.Clamp((int)Math.Floor(e.Y1 / celula), 0, linhas - 1);
        for (var l = l0; l <= l1; l++)
            for (var c = c0; c <= c1; c++) yield return l * colunas + c;
    }

    /// <summary>Os índices dos contornos postos que se sobrepõem a <paramref name="p"/>.</summary>
    IEnumerable<int> Colisoes(PontoTela[] p)
    {
        var env = Rotulagem.Envolvente(p);
        var vistos = new HashSet<int>();
        foreach (var k in Celulas(env))
            if (grade[k] is { } l)
                foreach (var i in l)
                    if (vistos.Add(i) && postos[i].Env.Intersecta(env) && Rotulagem.Sobrepoe(p, postos[i].Poli)) yield return i;
    }

    public bool Livre(PontoTela[] p) => !Colisoes(p).Any();
    public bool Cabe(PontoTela[] p) => DentroDaTela(p) && Livre(p);

    /// <summary>Quantos contornos postos <paramref name="p"/> cobriria.</summary>
    public int Sobrepostos(PontoTela[] p) => Colisoes(p).Count();

    /// <summary>Marca a área como ocupada, sem conferir (obstáculos: o lote, a rosa dos ventos, a escala, o balão).</summary>
    public void Ocupar(PontoTela[] p)
    {
        var i = postos.Count;
        var env = Rotulagem.Envolvente(p);
        postos.Add((p, env));
        foreach (var k in Celulas(env)) (grade[k] ??= []).Add(i);
    }

    public void Ocupar(Caixa c) => Ocupar(c.Contorno());

    /// <summary>Põe o primeiro candidato que cabe; devolve o índice dele, ou −1 (nenhum cabe: o rótulo fica oculto).</summary>
    public int Tentar(IReadOnlyList<CandidatoRotulo> candidatos)
    {
        for (var i = 0; i < candidatos.Count; i++)
        {
            var p = candidatos[i].Contorno();
            if (!Cabe(p)) continue;
            Ocupar(p);
            return i;
        }
        return -1;
    }
}

/// <summary>A colocação de rótulos do mapa sem sobreposição (2026-11-17), determinística e sem dependência de desenho, para testar. Ordem de
/// prioridade: a etiqueta vermelha do lote e o balão, depois os nomes de ruas e do curso d'água, a cota, os nomes dos confrontantes e os
/// números dos lotes, e por fim os nomes das zonas. Cada rótulo tenta as suas posições em ordem; se nenhuma couber inteira na tela sem
/// cobrir o que já foi posto, ele não aparece (nunca sai cortado nem por cima de outro).</summary>
public static class Rotulagem
{
    /// <summary>As prioridades (menor = mais importante).</summary>
    public const int PrioridadeMarcador = 0, PrioridadeRua = 1, PrioridadeCota = 2, PrioridadeLote = 3, PrioridadeOutro = 4;

    /// <summary>Cantos do retângulo w × h centrado em c, girado ang graus (sentido anti-horário, como o rotate do PIL).</summary>
    public static PontoTela[] CaixaGirada(PontoTela c, double w, double h, double ang)
    {
        var a = ang * Math.PI / 180;
        double co = Math.Cos(a), si = Math.Sin(a);
        return new[] { (-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2) }
            .Select(q => new PontoTela(c.X + q.Item1 * co + q.Item2 * si, c.Y - q.Item1 * si + q.Item2 * co)).ToArray();
    }

    /// <summary>Dois polígonos convexos se sobrepõem (com área)? Teorema do eixo separador; encostar não conta.</summary>
    public static bool Sobrepoe(PontoTela[] p, PontoTela[] q)
    {
        foreach (var poli in new[] { p, q })
            for (var i = 0; i < poli.Length; i++)
            {
                var (a, c) = (poli[i], poli[(i + 1) % poli.Length]);
                double nx = c.Y - a.Y, ny = a.X - c.X;
                double pMin = double.MaxValue, pMax = double.MinValue, qMin = double.MaxValue, qMax = double.MinValue;
                foreach (var v in p) { var t = nx * v.X + ny * v.Y; pMin = Math.Min(pMin, t); pMax = Math.Max(pMax, t); }
                foreach (var v in q) { var t = nx * v.X + ny * v.Y; qMin = Math.Min(qMin, t); qMax = Math.Max(qMax, t); }
                if (pMax <= qMin || qMax <= pMin) return false;
            }
        return true;
    }

    public static Caixa Envolvente(IReadOnlyList<PontoTela> p) => new(p.Min(v => v.X), p.Min(v => v.Y), p.Max(v => v.X), p.Max(v => v.Y));

    /// <summary>O centro e oito posições em volta (abaixo, acima, direita, esquerda e os cantos), afastadas de <paramref name="passo"/>.</summary>
    public static IReadOnlyList<CandidatoRotulo> AoRedor(PontoTela c, double w, double h, double passo, string? texto = null)
    {
        (double, double)[] dir = [(0, 0), (0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (-1, 1), (1, -1), (-1, -1)];
        return dir.Select(d => new CandidatoRotulo(new PontoTela(c.X + d.Item1 * (w / 2 + passo), c.Y + d.Item2 * (h / 2 + passo)), w, h, 0, texto)).ToList();
    }

    /// <summary>Põe os pedidos por prioridade (empate: a ordem de chegada), sobre os obstáculos fixos. Devolve só os que couberam.</summary>
    public static IReadOnlyList<RotuloPosto> Colocar(double largura, double altura, IEnumerable<PedidoRotulo> pedidos, IEnumerable<PontoTela[]>? obstaculos = null, double margem = 2)
    {
        var q = new QuadroRotulos(largura, altura, margem);
        foreach (var o in obstaculos ?? []) q.Ocupar(o);
        return Colocar(q, pedidos);
    }

    /// <summary>Como o outro <c>Colocar</c>, num quadro que já tem rótulos postos.</summary>
    public static IReadOnlyList<RotuloPosto> Colocar(QuadroRotulos q, IEnumerable<PedidoRotulo> pedidos)
    {
        var postos = new List<RotuloPosto>();
        foreach (var p in pedidos.Select((p, i) => (p, i)).OrderBy(x => x.p.Prioridade).ThenBy(x => x.i).Select(x => x.p))
        {
            var i = q.Tentar(p.Candidatos);
            if (i >= 0) postos.Add(new RotuloPosto(p.Id, p.Prioridade, i, p.Candidatos[i], p.Candidatos[i].Contorno()));
        }
        return postos;
    }

    /// <summary>O lugar do balão de dados do lote. Candidatos: encostado ao lote (abaixo, acima, à direita e à esquerda, centrado e alinhado
    /// às bordas, puxado para dentro da tela) e, por último, uma grade na tela inteira. Ordem: o que não cobre nada de
    /// <paramref name="proibidos"/> (o lote com a folga, a etiqueta do lote, a escala, a rosa dos ventos); depois o que cobre menos
    /// <paramref name="rotulos"/>; depois os lados do lote antes da grade; depois o mais perto do lote. O balão sempre fica dentro da tela
    /// (se couber nela).</summary>
    public static (Caixa Caixa, LadoBalao Lado) EscolherBalao(Caixa alvo, double largura, double altura, double telaL, double telaA,
        IReadOnlyList<Caixa> proibidos, IReadOnlyList<PontoTela[]> rotulos, double folga, double margem)
    {
        double Cx(double x) => Math.Clamp(x, margem, Math.Max(margem, telaL - largura - margem));
        double Cy(double y) => Math.Clamp(y, margem, Math.Max(margem, telaA - altura - margem));
        var cands = new List<(Caixa C, LadoBalao Lado, int Ordem)>();
        void Por(double x, double y, LadoBalao lado, int ordem)
        {
            var c = new Caixa(Cx(x), Cy(y), Cx(x) + largura, Cy(y) + altura);
            if (!cands.Any(o => o.C == c)) cands.Add((c, lado, ordem));
        }
        var cen = alvo.Centro;
        double[] xsH = [cen.X - largura / 2, alvo.X0, alvo.X1 - largura];
        double[] ysV = [cen.Y - altura / 2, alvo.Y0, alvo.Y1 - altura];
        foreach (var x in xsH) Por(x, alvo.Y1 + folga, LadoBalao.Abaixo, 0);
        foreach (var x in xsH) Por(x, alvo.Y0 - folga - altura, LadoBalao.Acima, 1);
        foreach (var y in ysV) Por(alvo.X1 + folga, y, LadoBalao.Direita, 2);
        foreach (var y in ysV) Por(alvo.X0 - folga - largura, y, LadoBalao.Esquerda, 3);
        var passo = Math.Max(8, telaL / 40);
        for (var y = margem; y <= telaA - altura - margem + 1e-9; y += Math.Max(passo, (telaA - altura - 2 * margem) / 2))
            for (var x = margem; x <= telaL - largura - margem + 1e-9; x += passo) Por(x, y, LadoBalao.Livre, 4);
        if (cands.Count == 0) Por(margem, margem, LadoBalao.Livre, 4);

        var alvoComFolga = alvo.Expandida(folga - 1e-6);
        double Duro(Caixa c) => proibidos.Sum(c.AreaComum) + c.AreaComum(alvoComFolga);
        int Leves(Caixa c) { var p = c.Contorno(); return rotulos.Count(r => Sobrepoe(p, r)); }
        double Dist(Caixa c) { var m = c.Centro; return Math.Sqrt((m.X - cen.X) * (m.X - cen.X) + (m.Y - cen.Y) * (m.Y - cen.Y)); }
        var melhor = cands.OrderBy(c => Duro(c.C)).ThenBy(c => Leves(c.C)).ThenBy(c => c.Ordem == 4 ? 1 : 0).ThenBy(c => Dist(c.C)).ThenBy(c => c.Ordem).First();
        return (melhor.C, melhor.Lado);
    }
}
