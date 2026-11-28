namespace Pmvni.App;

public sealed record CartaoGrupoAnalise(string Numero, string Nome, string Descricao, SeletoresPerfilPlugin Seletores, string Icone)
{
    internal static (string Icone, string Descricao) Explicar(string nome, int[] secoes) => nome switch
    {
        "Instrução e documentação" => (Glifos.Documento, "Confere o pedido, as versões das pranchas, as assinaturas e a responsabilidade técnica indicada na ART ou no RRT."),
        "Imóvel e titularidade" => (Glifos.Mapa, "Cruza matrícula, cadastro municipal e peças do projeto para identificar o lote e verificar o vínculo do requerente com o imóvel."),
        "Terreno, topografia e passeio" => (Glifos.Globo, "Compara o levantamento com a implantação e confere níveis, limites do terreno e condições da calçada."),
        "Enquadramento e usos especiais" => (Glifos.Livro, "Identifica a zona e o uso proposto e verifica quais regras urbanísticas e exigências específicas se aplicam à atividade."),
        "Representação, cotas e compatibilização" => (Glifos.Figuras, "Confronta plantas, cortes e fachadas para localizar cotas incompatíveis, medidas ausentes e diferenças entre desenhos."),
        "Áreas e índices" => (Glifos.Grafico, "Reconcilia o quadro de áreas com os desenhos e confere os cálculos de ocupação, aproveitamento e permeabilidade."),
        "Volumetria e afastamentos" => (Glifos.PaginaInteira, "Verifica alturas, pavimentos e projeções da edificação e mede os afastamentos em relação às divisas."),
        "Ambientes, iluminação e esquadrias" => (Glifos.Figuras, "Confere as dimensões dos ambientes e calcula quanto as portas e janelas contribuem para a iluminação e a ventilação."),
        "Circulações e escadas" => (Glifos.Avancar, "Examina larguras e desníveis dos percursos, escadas e rampas, além de elevadores e proteções contra quedas."),
        "Acessibilidade" => (Glifos.Conferir, "Acompanha a rota acessível desde o ingresso e verifica passagens, áreas de manobra e uso dos ambientes e sanitários."),
        "Estacionamento e manobra" => (Glifos.Mapa, "Confere a quantidade e as dimensões das vagas, os acessos de veículos, as áreas de manobra e a carga e descarga."),
        "Cobertura, drenagem e instalações" => (Glifos.Ferramenta, "Examina beirais, caimentos e destino das águas pluviais e verifica como as instalações se integram ao projeto."),
        "Incêndio" => (Glifos.Avisos, "Verifica as exigências de segurança contra incêndio e pânico e a necessidade de documentação do Corpo de Bombeiros."),
        "Reforma e regularização" => (Glifos.Alterados, "Distingue o que já existe, o que será demolido e o que será construído. Confronta essas áreas com as aprovações anteriores."),
        "Consolidação" => (Glifos.Conferir, "Audita os achados das 28 verificações, procura contraprovas e reúne evidências e lacunas para fechar o relatório."),
        _ => (Glifos.Lista, "Abrange as verificações " + string.Join(", ", secoes) + " do checklist do plugin.")
    };
}
