using PdfSharp.Drawing;

namespace Pmvni.Revisao;

public sealed record ObservacaoPrancha(string Texto, string Referencia)
{
    public static IReadOnlyList<ObservacaoPrancha> Predefinidas { get; } = Array.AsReadOnly<ObservacaoPrancha>(
    [
        new("A aprovação do projeto não implica reconhecimento do direito de propriedade, nem certifica os limites e dimensões do imóvel indicados na planta de situação.", "(Art. 16 da LC nº 70/1990)."),
        new("É proibida a instalação de padrão de energia elétrica no afastamento frontal obrigatório de edificações com atividade comercial no pavimento térreo.", "(Art. 68, § 3º, do PDM – Lei nº 1.382/2020)."),
        new("A aprovação do projeto não garante a disponibilidade de abastecimento de água e esgotamento sanitário, cabendo ao requerente verificar a viabilidade junto ao prestador competente, observadas as obrigações legais.", "(Arts. 115 a 117 da LC nº 70/1990)."),
        new("O responsável técnico declara que a cisterna, quando prevista, foi dimensionada conforme a ABNT NBR 5626 e a legislação aplicável.", "(Arts. 115 e 119 da LC nº 70/1990)."),
        new("A compatibilização entre o projeto aprovado pela Prefeitura e o projeto aprovado pelo CBMES, quando exigível, é de responsabilidade dos profissionais habilitados.", "(Art. 7º da LC nº 70/1990)."),
        new("O requerente e os profissionais responsáveis respondem, nos limites de suas atribuições, pelas informações ambientais prestadas e pelo cumprimento das licenças e condicionantes exigíveis, inclusive quanto às APPs.", "(Art. 6º do PDM e art. 7º da LC nº 70/1990)."),
        new("A obra deverá ser executada conforme o projeto aprovado e o alvará expedido, sob pena das sanções legalmente cabíveis.", "(Arts. 45 e 52, II, da LC nº 70/1990)."),
        new("O autor do projeto declara, sob sua responsabilidade técnica, a veracidade das informações e a conformidade do projeto com a legislação urbanística e edilícia vigente.", "(Arts. 7º e 52, I, da LC nº 70/1990)."),
    ]);

    public static double Desenhar(XGraphics grafico, XRect limites, IReadOnlyList<ObservacaoPrancha> itens)
    {
        if (itens.Count == 0) return 0;
        var normal = new XFont("Arial", 9);
        var negrito = new XFont("Arial", 9, XFontStyleEx.Bold);
        const double alturaLinha = 12;
        var trechos = new List<(string Texto, XFont Fonte, double X, double Base)>();
        double x = 0, y = alturaLinha;
        void Acrescentar(string texto, XFont fonte)
        {
            var espaco = grafico.MeasureString(" ", fonte).Width;
            foreach (var palavra in texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var largura = grafico.MeasureString(palavra, fonte).Width;
                if (largura > limites.Width)
                    throw new ArgumentException("Uma palavra da observação excede a largura disponível.");
                if (x > 0 && x + largura > limites.Width)
                {
                    x = 0;
                    y += alturaLinha;
                }
                trechos.Add((palavra, fonte, x, y));
                x += largura + espaco;
            }
        }
        Acrescentar("OBSERVAÇÕES:", negrito);
        for (int i = 0; i < itens.Count; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itens[i].Texto);
            x = 0;
            y += alturaLinha + 4;
            Acrescentar($"{i + 1:00} – {itens[i].Texto}", normal);
            Acrescentar(itens[i].Referencia, negrito);
        }
        if (y + 3 > limites.Height)
            throw new ArgumentException("As observações excedem a altura disponível na prancha.");
        foreach (var trecho in trechos)
            grafico.DrawString(trecho.Texto, trecho.Fonte, XBrushes.Black,
                limites.X + trecho.X, limites.Y + trecho.Base);
        return y + 3;
    }
}
