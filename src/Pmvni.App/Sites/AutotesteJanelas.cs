using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Autoteste das janelas dos sites (1.0.0): reproduz, com uma página fictícia (sem rede, sem portal e sem dados reais), o que o
/// visualizador de documentos do Essencial faz — window.open de uma janela vazia, document.write do modelo nela e leitura dos dados do
/// documento em window.opener — e confere que a janela auxiliar (<see cref="JanelaPopupSite"/>) recebeu o pedido, preencheu o modelo com o
/// opener, fechou com window.close() e que a página de origem continuou a mesma. Entra no "ok" do --autoteste do build oficial.</summary>
internal static class AutotesteJanelas
{
    const string Nome = "Documento fictício 123";

    const string Pagina = """
        <!doctype html><html><head><meta charset="utf-8"><title>Origem PMVNI</title></head><body><p>Protocolo fictício</p>
        <script>
        window.documento = { nome: "Documento fictício 123", paginas: 3 };
        setTimeout(function () {
          var w = window.open("", "visualizador", "width=640,height=480");
          if (!w) { chrome.webview.postMessage(JSON.stringify({ erro: "window.open devolveu null" })); return; }
          w.document.write('<!doctype html><html><head><meta charset="utf-8"><title>Visualizador</title></head><body>'
            + '<p id="nome">{{DATA.DOCUMENTO.NOME}}</p><p id="paginas">{{paginas.total}}</p><script>'
            + 'var d = window.opener && window.opener.documento;'
            + 'if (d) { document.getElementById("nome").textContent = d.nome; document.getElementById("paginas").textContent = String(d.paginas); }'
            + '<\/script></body></html>');
          w.document.close();
          setTimeout(function () {
            var r = { nome: w.document.getElementById("nome").textContent, paginas: w.document.getElementById("paginas").textContent,
                      opener: w.opener === window, titulo: document.title };
            w.close();
            chrome.webview.postMessage(JSON.stringify(r));
          }, 800);
        }, 300);
        </script></body></html>
        """;

    public static async Task<Dictionary<string, object?>> ConferirAsync()
    {
        var r = new Dictionary<string, object?> { ["ok"] = false };
        Window? origem = null;
        try
        {
            origem = new Window { Title = "Autoteste das janelas dos sites" };
            var web = new WebView2();
            origem.Content = web;
            try { origem.AppWindow.Resize(new Windows.Graphics.SizeInt32(480, 320)); } catch (Exception) { }
            origem.Activate();
            // O mesmo ambiente e o mesmo perfil dos sites (como no app de verdade).
            await web.EnsureCoreWebView2Async(await PaginaSites.Ambiente());
            var core = web.CoreWebView2;
            var pedidos = 0;
            core.NewWindowRequested += (s, e) => { pedidos++; JanelaPopupSite.Atender(s, e, "Autoteste", ElementTheme.Default, null); };
            var resposta = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            core.WebMessageReceived += (_, e) => { try { resposta.TrySetResult(e.TryGetWebMessageAsString()); } catch (Exception x) { resposta.TrySetException(x); } };
            core.NavigateToString(Pagina);
            var texto = await resposta.Task.WaitAsync(TimeSpan.FromSeconds(40));
            var abertasDepoisDoPedido = JanelaPopupSite.Abertas;
            for (var i = 0; i < 60 && JanelaPopupSite.Abertas > 0; i++) await Task.Delay(100);
            using var d = JsonDocument.Parse(texto);
            var raiz = d.RootElement;
            string S(string p) => raiz.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var opener = raiz.TryGetProperty("opener", out var o) && o.ValueKind == JsonValueKind.True;
            var titulo = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("document.title")) ?? "";
            r["pedidos_de_janela"] = pedidos;
            r["modelo_preenchido"] = S("nome") == Nome && S("paginas") == "3";
            r["texto_no_visualizador"] = S("nome");
            r["opener_preservado"] = opener;
            r["janela_fechou_com_window_close"] = JanelaPopupSite.Abertas == 0;
            r["janelas_abertas_no_pedido"] = abertasDepoisDoPedido;
            r["origem_intacta"] = titulo == "Origem PMVNI" && S("titulo") == "Origem PMVNI";
            if (raiz.TryGetProperty("erro", out var erro)) r["erro"] = erro.GetString();
            r["ok"] = pedidos == 1 && (bool)r["modelo_preenchido"]! && opener && JanelaPopupSite.Abertas == 0 && (bool)r["origem_intacta"]!;
        }
        catch (Exception e) { r["erro"] = e.GetType().Name + ": " + e.Message; }
        finally
        {
            JanelaPopupSite.FecharTodas();
            try { origem?.Close(); } catch (Exception) { }
        }
        return r;
    }
}
