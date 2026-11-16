using System.Buffers.Binary;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Pmvni.Imoveis.Testes;

/// <summary>Uma base QGIS inventada (nomes, inscrições e coordenadas fictícios), com as mesmas camadas e campos da base municipal:
/// uma quadra de quatro lotes, uma rua, a BR-262, um curso d'água com faixa de APP, duas zonas, AUC, perímetro urbano e o cadastro.
/// O lote 4 (30 × 40 m) é o do teste: a APP e a faixa não edificável da rodovia o atingem.</summary>
static class BaseFicticia
{
    public const string Lote4 = "01020030004001", Lote3 = "01020030003001", Lote5 = "01020030005001", Lote12 = "01020030012001";
    const double X0 = 300_000, Y0 = 7_750_000;

    static (double, double)[] Ret(double x0, double y0, double x1, double y1) =>
        [(X0 + x0, Y0 + y0), (X0 + x0, Y0 + y1), (X0 + x1, Y0 + y1), (X0 + x1, Y0 + y0), (X0 + x0, Y0 + y0)]; // sentido horário: casca
    static (double, double)[] Linha(params (double X, double Y)[] p) => p.Select(q => (X0 + q.X, Y0 + q.Y)).ToArray();

    public static string Criar()
    {
        var pasta = Directory.CreateTempSubdirectory("pmvni-base-ficticia").FullName;
        var shapes = Directory.CreateDirectory(Path.Combine(pasta, "shapes")).FullName;
        var lot = Directory.CreateDirectory(Path.Combine(pasta, "SHAPE_LOTEAMENTOS")).FullName;
        var zon = Directory.CreateDirectory(Path.Combine(pasta, "2025_10")).FullName;
        var agerh = Directory.CreateDirectory(Path.Combine(pasta, "HIDROGRAFIA COM NOMES (AGERH-GEOBASES)")).FullName;

        Gravar(Path.Combine(shapes, "Lotes.shp"), 5, ["InscImob", "AREA"],
            (Ret(100, 100, 130, 140), [Lote4, "1200.00"]), (Ret(70, 100, 100, 140), [Lote3, "1200.00"]),
            (Ret(130, 100, 160, 140), [Lote5, "1200.00"]), (Ret(100, 140, 130, 180), [Lote12, "1200.00"]));
        Gravar(Path.Combine(shapes, "Edificacao.shp"), 5, ["InscImob", "area"],
            (Ret(105, 105, 120, 125), [Lote4, "300.00"]), (Ret(75, 110, 90, 130), [Lote3, "300.00"]));
        Gravar(Path.Combine(shapes, "Logradouro.shp"), 3, ["id", "NomeLogra"],
            (Linha((-200, 90), (500, 90)), ["1", "RUA DAS ACACIAS"]), (Linha((-200, 25), (500, 95)), ["2", "BR 262"]),
            (Linha((190, -200), (190, 500)), ["3", "AVENIDA PREFEITO FULANO DE TAL"]));
        Gravar(Path.Combine(shapes, "Hidro_app.shp"), 3, ["TIPO", "APP"], (Linha((-200, 175), (500, 140)), ["rio", "1530"]));
        Gravar(Path.Combine(agerh, "Cursos_dagua_AGERH_VNI.shp"), 3, ["nom_hidrog"], (Linha((-200, 176), (500, 141)), ["Córrego Fictício"]));
        Gravar(Path.Combine(zon, "Zoneamento_2025_10.shp"), 5, ["ZONA"], (Ret(-60, 45, 150, 225), ["ZCN2"]), (Ret(150, 45, 235, 225), ["ZRE"]), (Ret(-300, -300, 600, 45), ["ZEIS"]));
        Gravar(Path.Combine(lot, "Quadras.shp"), 5, ["QUADRA", "ZONA"], (Ret(70, 100, 160, 180), ["Q7", "ZCN2"]));
        Gravar(Path.Combine(lot, "NUMERO DO LOTE.shp"), 1, ["LOTE"],
            (Linha((115, 130)), ["4"]), (Linha((85, 120)), ["3"]), (Linha((145, 120)), ["5"]), (Linha((115, 160)), ["12"]));
        Gravar(Path.Combine(lot, "NOME DO LOTEAMENTO.shp"), 5, ["nome"], (Ret(60, 95, 170, 185), ["Loteamento Exemplo"]));
        Gravar(Path.Combine(shapes, "AUC.shp"), 5, ["id", "Perímetro"], (Ret(-300, -300, 600, 600), ["1", "Sede"]));
        Gravar(Path.Combine(shapes, "Per_urb_2022.shp"), 5, ["NOME"], (Ret(-300, -300, 600, 600), ["SEDE"]));

        var db = Path.Combine(Directory.CreateDirectory(Path.Combine(pasta, "BASE_CADASTRAL_AUXILIAR")).FullName, "Base_Cadastral_Auxiliar.sqlite");
        using (var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString()))
        {
            con.Open();
            using var c = con.CreateCommand();
            c.CommandText = """
                CREATE TABLE cadastro_imobiliario (linha_fonte INTEGER, inscricao_municipal TEXT, inscricao_reduzida TEXT, nome_do_contribuinte TEXT,
                    tipo_do_logradouro TEXT, logradouro TEXT, n_metrico TEXT, bairro TEXT, situacao TEXT, ocupacao TEXT, matricula TEXT,
                    ultilizacao_do_imovel TEXT, area_unidade_m2 TEXT, loteamento TEXT);
                INSERT INTO cadastro_imobiliario VALUES
                    (1, '01020030004001', '4321', 'FULANO DE TAL EXEMPLO', 'RUA', 'DAS ACACIAS', '120', 'CENTRO', 'Ativo', 'CONSTRUIDO', '9.999', 'Residencial', '150.00', 'Loteamento Exemplo'),
                    (2, '01020030003001', '4320', 'BELTRANA DE SOUZA MODELO E SOCIO (A)(S)', 'RUA', 'DAS ACACIAS', '110', 'CENTRO', 'Ativo', 'CONSTRUIDO', NULL, 'Residencial', '90.00', NULL),
                    (3, '01020030005001', '4322', 'SICRANO PEREIRA TESTE', 'RUA', 'DAS ACACIAS', '130', 'CENTRO', 'Ativo', 'VAGO', NULL, 'Territorial', NULL, NULL);
                """;
            c.ExecuteNonQuery();
        }
        return pasta;
    }

    /// <summary>Grava um shapefile (.shp e .dbf, com .cpg UTF-8) de pontos (1), linhas (3) ou polígonos (5), uma parte por feição.</summary>
    static void Gravar(string shp, int tipo, string[] campos, params ((double X, double Y)[] Pts, string[] Valores)[] feicoes)
    {
        using (var f = new MemoryStream())
        {
            f.Write(new byte[100]);
            Span<byte> cab = stackalloc byte[8];
            for (var n = 0; n < feicoes.Length; n++)
            {
                var pts = feicoes[n].Pts;
                byte[] c;
                if (tipo == 1)
                {
                    c = new byte[20];
                    BinaryPrimitives.WriteInt32LittleEndian(c, 1);
                    BinaryPrimitives.WriteDoubleLittleEndian(c.AsSpan(4), pts[0].X);
                    BinaryPrimitives.WriteDoubleLittleEndian(c.AsSpan(12), pts[0].Y);
                }
                else
                {
                    c = new byte[44 + 4 + pts.Length * 16];
                    BinaryPrimitives.WriteInt32LittleEndian(c, tipo);
                    BinaryPrimitives.WriteDoubleLittleEndian(c.AsSpan(4), pts.Min(p => p.X));
                    BinaryPrimitives.WriteDoubleLittleEndian(c.AsSpan(12), pts.Min(p => p.Y));
                    BinaryPrimitives.WriteDoubleLittleEndian(c.AsSpan(20), pts.Max(p => p.X));
                    BinaryPrimitives.WriteDoubleLittleEndian(c.AsSpan(28), pts.Max(p => p.Y));
                    BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(36), 1);
                    BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(40), pts.Length);
                    for (var i = 0; i < pts.Length; i++)
                    {
                        BinaryPrimitives.WriteDoubleLittleEndian(c.AsSpan(48 + i * 16), pts[i].X);
                        BinaryPrimitives.WriteDoubleLittleEndian(c.AsSpan(56 + i * 16), pts[i].Y);
                    }
                }
                BinaryPrimitives.WriteInt32BigEndian(cab, n + 1);
                BinaryPrimitives.WriteInt32BigEndian(cab[4..], c.Length / 2);
                f.Write(cab);
                f.Write(c);
            }
            File.WriteAllBytes(shp, f.ToArray());
        }

        const int tam = 60;
        var tamReg = 1 + campos.Length * tam;
        var tamCab = 32 + campos.Length * 32 + 1;
        var dbf = new byte[tamCab + feicoes.Length * tamReg];
        BinaryPrimitives.WriteInt32LittleEndian(dbf.AsSpan(4), feicoes.Length);
        BinaryPrimitives.WriteInt16LittleEndian(dbf.AsSpan(8), (short)tamCab);
        BinaryPrimitives.WriteInt16LittleEndian(dbf.AsSpan(10), (short)tamReg);
        for (var k = 0; k < campos.Length; k++)
        {
            Encoding.Latin1.GetBytes(campos[k]).CopyTo(dbf, 32 + k * 32);
            dbf[32 + k * 32 + 11] = (byte)'C';
            dbf[32 + k * 32 + 16] = tam;
        }
        dbf[tamCab - 1] = 0x0D;
        for (var n = 0; n < feicoes.Length; n++)
        {
            var o = tamCab + n * tamReg;
            dbf.AsSpan(o, tamReg).Fill((byte)' ');
            for (var k = 0; k < campos.Length; k++) Encoding.UTF8.GetBytes(feicoes[n].Valores[k]).CopyTo(dbf, o + 1 + k * tam);
        }
        File.WriteAllBytes(Path.ChangeExtension(shp, ".dbf"), dbf);
        File.WriteAllText(Path.ChangeExtension(shp, ".cpg"), "UTF-8");
    }
}
