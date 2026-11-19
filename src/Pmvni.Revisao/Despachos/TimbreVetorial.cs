using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Pmvni.Revisao;

public static class TimbreVetorial
{
    public static byte[] Aplicar(byte[] modelo)
    {
        using var copia = new MemoryStream();
        copia.Write(modelo);
        using (var zip = new ZipArchive(copia, ZipArchiveMode.Update, leaveOpen: true))
        {
            var alvos = new Dictionary<string, string>();
            foreach (var imagem in zip.Entries.Where(e => e.FullName.StartsWith("word/media/") && e.Name.EndsWith(".png")).ToArray())
            {
                string hash;
                using (var original = imagem.Open()) hash = Convert.ToHexStringLower(SHA256.HashData(original));
                using var svg = typeof(TimbreVetorial).Assembly.GetManifestResourceStream($"Pmvni.Timbre-{hash}.svg");
                if (svg is null) continue;
                string alvo = Path.ChangeExtension(imagem.FullName, ".svg");
                using var destino = zip.CreateEntry(alvo).Open();
                svg.CopyTo(destino);
                alvos[imagem.FullName["word/".Length..]] = alvo["word/".Length..];
            }
            if (alvos.Count == 0) return modelo;
            foreach (var parte in zip.Entries.Where(e => e.FullName.StartsWith("word/_rels/") && e.Name.EndsWith(".rels")).ToArray())
            {
                using var stream = parte.Open();
                var rels = XDocument.Load(stream);
                bool mudou = false;
                foreach (var rel in rels.Root!.Elements())
                {
                    if (!alvos.TryGetValue((string?)rel.Attribute("Target") ?? "", out var alvo)) continue;
                    rel.SetAttributeValue("Target", alvo);
                    mudou = true;
                }
                if (mudou) Salvar(stream, rels);
            }
            using var tipos = zip.GetEntry("[Content_Types].xml")!.Open();
            var xml = XDocument.Load(tipos);
            var raiz = xml.Root!;
            if (!raiz.Elements().Any(x => (string?)x.Attribute("Extension") == "svg"))
            {
                raiz.Add(new XElement(raiz.Name.Namespace + "Default", new XAttribute("Extension", "svg"), new XAttribute("ContentType", "image/svg+xml")));
                Salvar(tipos, xml);
            }
        }
        return copia.ToArray();
    }

    private static void Salvar(Stream stream, XDocument doc)
    {
        stream.Position = 0;
        stream.SetLength(0);
        doc.Save(stream);
    }
}
