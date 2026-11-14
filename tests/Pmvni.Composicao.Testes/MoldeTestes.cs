using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Composicao.Testes;

public class MoldeTestes
{
    private static string Render(string fonte, IDictionary<string, object?> ctx) =>
        Molde.Compilar(fonte).Renderizar(ctx, Molde.FiltrosBasicos);

    private static Dictionary<string, object?> Ctx(params (string, object?)[] itens)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in itens) d[k] = v;
        return d;
    }

    [Fact]
    public void escape_igual_ao_python()
    {
        Assert.Equal("a&amp;b&lt;c&gt;&quot;d&#x27;", PythonTexto.Escape("a&b<c>\"d'"));
        Assert.Equal("&amp;lt;", PythonTexto.Escape("&lt;"));
    }

    [Fact]
    public void loop_even_e_n2()
    {
        var ctx = Ctx(("xs", new List<object?> { "a", "b", "c" }));
        var r = Render("{% for x in xs %}{{ loop.index }}{{ loop.n2 }}{{ loop.even }}{{ loop.first }}{{ loop.last }},{% endfor %}", ctx);
        Assert.Equal("101FalseTrueFalse,202TrueFalseFalse,303FalseFalseTrue,", r);
    }

    [Fact]
    public void if_else_com_lista_vazia()
    {
        const string t = "{% if xs %}cheia{% else %}vazia{% endif %}";
        Assert.Equal("vazia", Render(t, Ctx(("xs", new List<object?>()))));
        Assert.Equal("cheia", Render(t, Ctx(("xs", new List<object?> { 0L }))));
        Assert.Equal("vazia", Render(t, Ctx()));
        Assert.Equal("vazia", Render(t, Ctx(("xs", new Dictionary<string, object?>()))));
    }

    [Fact]
    public void filtro_safe_nao_escapa()
    {
        var ctx = Ctx(("h", "<b>&</b>"));
        Assert.Equal("<b>&</b>", Render("{{ h|safe }}", ctx));
        Assert.Equal("&lt;b&gt;&amp;&lt;/b&gt;", Render("{{ h }}", ctx));
        Assert.Equal("", Render("{{ nada|safe }}", ctx));
    }

    [Fact]
    public void caminho_com_indice_numerico()
    {
        var ctx = Ctx(("a", Ctx(("b", new List<object?> { "x", Ctx(("c", "y")) }))));
        Assert.Equal("x", Render("{{ a.b.0 }}", ctx));
        Assert.Equal("y", Render("{{ a.b.1.c }}", ctx));
        Assert.Equal("", Render("{{ a.b.2 }}", ctx));
        Assert.Equal("", Render("{{ a.b.2.c }}", ctx));
        Assert.Equal("", Render("{{ a.b.x }}", ctx));
        Assert.Equal("", Render("{{ a.zz.0 }}", ctx));
    }

    [Fact]
    public void marcador_desconhecido_lanca()
    {
        var e = Assert.ThrowsAny<Exception>(() => Molde.Compilar("{% while x %}"));
        Assert.Equal("Marcador desconhecido no modelo: {% while x %}", e.Message);
        e = Assert.ThrowsAny<Exception>(() => Molde.Compilar("{% endif %}"));
        Assert.Equal("Marcador desconhecido no modelo: {% endif %}", e.Message);
    }

    [Fact]
    public void bloco_sem_fechamento_lanca()
    {
        var e = Assert.ThrowsAny<Exception>(() => Molde.Compilar("{% for x in y %}abc"));
        Assert.Equal("Bloco sem fechamento no modelo: endfor", e.Message);
        e = Assert.ThrowsAny<Exception>(() => Molde.Compilar("{% if x %}abc"));
        Assert.Equal("Bloco sem fechamento no modelo: else/endif", e.Message);
    }

    [Fact]
    public void filtro_desconhecido_lanca()
    {
        var e = Assert.ThrowsAny<Exception>(() => Render("{{ x|zzz }}", Ctx(("x", 1L))));
        Assert.Equal("Filtro desconhecido: zzz", e.Message);
    }

    [Fact]
    public void str_de_bool_e_float()
    {
        Assert.Equal("True", PythonTexto.Str(true));
        Assert.Equal("False", PythonTexto.Str(false));
        Assert.Equal("0.1", PythonTexto.Str(0.1));
        Assert.Equal("1e-05", PythonTexto.Str(1e-05));
        Assert.Equal("12.0", PythonTexto.Str(12.0));
        Assert.Equal("", PythonTexto.Str(null));
        Assert.Equal("12", PythonTexto.Str(12L));
        Assert.Equal("1e+16", PythonTexto.Str(1e16));
        Assert.Equal("1000000000000000.0", PythonTexto.Str(1e15));
        Assert.Equal("0.0001", PythonTexto.Str(0.0001));
        Assert.Equal("1.2345678901234568e+17", PythonTexto.Str(123456789012345678.0));
        Assert.Equal("-0.0", PythonTexto.Str(-0.0));
        Assert.Equal("inf", PythonTexto.Str(double.PositiveInfinity));
        Assert.Equal("nan", PythonTexto.Str(double.NaN));
    }

    [Fact]
    public void none_vira_vazio_e_valores_python()
    {
        var ctx = Ctx(("n", null), ("t", true), ("f", 2.5), ("i", 7L));
        Assert.Equal("[|True|2.5|7]", Render("[{{ n }}|{{ t }}|{{ f }}|{{ i }}]", ctx));
    }

    [Fact]
    public void not_literais_comentarios_e_espacos()
    {
        var ctx = Ctx(("x", ""), ("y", "v"));
        Assert.Equal("A", Render("{% if not x %}A{% else %}B{% endif %}", ctx));
        Assert.Equal("B", Render("{% if not y %}A{% else %}B{% endif %}", ctx));
        Assert.Equal("ab", Render("a{# comentário\n multi #}b", ctx));
        Assert.Equal("lit", Render("{{ 'lit' }}", ctx));
        Assert.Equal("LIT", Render("{{ \"lit\"|upper }}", ctx));
        Assert.Equal("v", Render("{{   y   }}", ctx));
        Assert.Equal("V", Render("{{ y | upper }}", ctx));
        Assert.Equal("v", Render("{%if y%}{{y}}{%endif%}", ctx));
    }

    [Fact]
    public void filtros_basicos()
    {
        var ctx = Ctx(("n", 3L), ("s", "7"), ("f", 3.9), ("t", "AbÇ"), ("z", null));
        Assert.Equal("03", Render("{{ n|n2 }}", ctx));
        Assert.Equal("07", Render("{{ s|n2 }}", ctx));
        Assert.Equal("03", Render("{{ f|n2 }}", ctx));
        Assert.Equal("abç", Render("{{ t|lower }}", ctx));
        Assert.Equal("ABÇ", Render("{{ t|upper }}", ctx));
        Assert.Equal("none", Render("{{ z|lower }}", ctx));
    }

    [Fact]
    public void filtros_extras_sao_usados_e_seguro_aplicado()
    {
        var filtros = new Dictionary<string, Func<object?, object?>>(Molde.FiltrosBasicos)
        {
            ["neg"] = v => new Seguro("<i>" + PythonTexto.Str(v) + "</i>"),
        };
        var r = Molde.Compilar("{{ x|neg }}|{{ x|neg|lower }}").Renderizar(Ctx(("x", "A")), filtros);
        Assert.Equal("<i>A</i>|&lt;i&gt;a&lt;/i&gt;", r);
    }

    [Fact]
    public void for_sobre_nulo_e_contexto_nao_alterado()
    {
        var ctx = Ctx(("xs", null));
        Assert.Equal("", Render("{% for x in xs %}{{ x }}{% endfor %}", ctx));
        Assert.False(ctx.ContainsKey("loop"));
        Assert.False(ctx.ContainsKey("x"));
    }

    [Fact]
    public void for_aninhado_e_if_dentro()
    {
        var ctx = Ctx(("l", new List<object?> {
            Ctx(("i", new List<object?> { "a", "b" })),
            Ctx(("i", new List<object?>())) }));
        var r = Render("{% for g in l %}[{% for x in g.i %}{{ x }}{% if loop.last %}.{% else %},{% endif %}{% endfor %}]{% endfor %}", ctx);
        Assert.Equal("[a,b.][]", r);
    }

    // ---- paridade com o Python real (pulada se o Python ou o kit não existir) ----

    private static string? Python()
    {
        foreach (var nome in new[] { "python", "python3", "py" })
        {
            try
            {
                var psi = new ProcessStartInfo(nome, "-I -c \"print(1)\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi)!;
                if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } continue; }
                if (p.ExitCode == 0) return nome;
            }
            catch { }
        }
        return null;
    }

    private static string[]? RodarPython(string codigo, string entradaJson) => RodarPythonComArg(codigo, entradaJson, "");

    private static object? Plano(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? (object)l : e.GetDouble(),
        JsonValueKind.Array => e.EnumerateArray().Select(Plano).ToList(),
        _ => e.EnumerateObject().ToDictionary(p => p.Name, p => Plano(p.Value)),
    };

    [Fact]
    public void paridade_escape_e_str_com_python()
    {
        var textos = new[] { "a&b<c>\"d'", "", "&amp;", "ç ã é \U0001F600", "x'\"y" };
        var json = "{\"textos\":" + JsonSerializer.Serialize(textos) +
            ",\"nums\":[0.1,1e-05,12.0,1e16,1e15,123456789.123,1.5e300,5e-324,0.0001,0.00001234,100.0,3,-7,0,-0.0,2.5e-7,1e22,0.30000000000000004]}";
        const string codigo = @"
import json, sys, html
d = json.loads(sys.stdin.buffer.read().decode('utf-8'))
saida = [html.escape(t) for t in d['textos']] + [str(n) for n in d['nums']]
print(json.dumps(saida))";
        var esperado = RodarPython(codigo, json);
        if (esperado is null) return;
        var doc = JsonDocument.Parse(json).RootElement;
        var obtido = textos.Select(PythonTexto.Escape)
            .Concat(doc.GetProperty("nums").EnumerateArray().Select(n => PythonTexto.Str(Plano(n)))).ToArray();
        Assert.Equal(esperado, obtido);
    }

    [Fact]
    public void paridade_render_com_python_do_kit()
    {
        var kit = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "OneDrive", "PMVNI", "SIDNEY JUNIOR", "KIT DE TREINAMENTO DE IA (PMVNI)", "02 - MODELOS DE DOCUMENTOS",
            "RELATORIO", "MODELO HTML (USADO PELOS PLUGINS)", "gerar_relatorio.py");
        if (!File.Exists(kit)) return;
        var casos = new[]
        {
            "{{ a }}|{{ b }}|{{ c }}|{{ d }}|{{ e }}|{{ f }}",
            "{% for x in l %}{{ loop.n2 }}{{ loop.even }}{{ x }};{% endfor %}",
            "{% if l %}s{% else %}n{% endif %}{% if not z %}Z{% endif %}{% if e %}E{% else %}e{% endif %}",
            "{{ a|lower }}{{ a|upper }}{{ b|n2 }}{{ f|n2 }}{{ 'q&'|safe }}{{ \"<\" }}{{ n|lower }}",
            "{{ l.0 }}{{ l.9 }}{{ o.k }}{{ o.l.1 }}{{ o.zz.1 }}{{ l }}{{ o }}",
            "{% for ch in a %}[{{ ch }}]{% endfor %}{% for k in o %}<{{ k }}>{% endfor %}",
            "{{ a|n2 }}", "{% foo %}", "{% if x %}abc", "{{ a|zzz }}", "{{ ß|upper }}{{ t|upper }}{{ s|lower }}",
        };
        var ctxJson = "{\"a\":\"A&<b>'\\\"\",\"b\":7,\"c\":true,\"d\":null,\"e\":\"\",\"f\":3.9,\"l\":[\"x\",\"y<\",0],\"z\":0," +
            "\"o\":{\"k\":\"v\",\"l\":[1,2.5]},\"n\":null,\"t\":\"straße ǰ\",\"s\":\"ΑΣ ΣΑΣ İ\",\"ß\":\"ß\"}";
        var json = "{\"casos\":" + JsonSerializer.Serialize(casos) + ",\"ctx\":" + ctxJson + "}";
        var codigo = @"
import json, sys, importlib.util
spec = importlib.util.spec_from_file_location('gr', sys.argv[1])
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
d = json.loads(sys.stdin.buffer.read().decode('utf-8'))
out = []
for c in d['casos']:
    try: out.append(m.render(m.compilar(c), d['ctx']))
    except Exception as e: out.append('ERR:' + str(e))
print(json.dumps(out))";
        // o caminho do kit vai como argumento: usa o mesmo helper, com argumento extra
        var esperado = RodarPythonComArg(codigo, json, kit);
        if (esperado is null) return;
        var ctx = (Dictionary<string, object?>)Plano(JsonDocument.Parse(ctxJson).RootElement)!;
        var obtido = casos.Select(c =>
        {
            try { return Molde.Compilar(c).Renderizar(ctx, Molde.FiltrosBasicos); }
            catch (Exception e) { return "ERR:" + e.Message; }
        }).ToArray();
        for (var i = 0; i < casos.Length; i++)
        {
            if (esperado[i].StartsWith("ERR:") && obtido[i].StartsWith("ERR:") && casos[i] == "{{ a|n2 }}") continue; // texto do int() difere
            Assert.True(esperado[i] == obtido[i], $"caso {i} ({casos[i]}): python=[{esperado[i]}] c#=[{obtido[i]}]");
        }
    }

    private static string[]? RodarPythonComArg(string codigo, string entradaJson, string arg)
    {
        var py = Python();
        if (py is null) return null;
        var psi = new ProcessStartInfo(py)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-I", "-B", "-c", codigo, arg }) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        using var p = Process.Start(psi)!;
        var bytes = new UTF8Encoding(false).GetBytes(entradaJson);
        p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
        p.StandardInput.Close();
        var saida = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(60000)) { try { p.Kill(); } catch { } throw new TimeoutException("Python"); }
        Assert.Equal(0, p.ExitCode);
        return JsonSerializer.Deserialize<string[]>(saida)!;
    }
}
