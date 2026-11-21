"""Servidor MCP local do kit. Um PDF e uma página por chamada, sem envio de documentos."""
import os
import tempfile
from pathlib import Path
import subprocess
import json
from mcp.server.mcpserver import MCPServer

server = MCPServer("Ferramentas PDF PMVNI")
base = Path(os.environ["LOCALAPPDATA"]) / "PMVNI/MotorPdf"


def pagina(pdf, numero):
    import pymupdf
    p = Path(pdf).resolve(strict=True)
    if p.suffix.lower() != ".pdf":
        raise ValueError("Escolha um PDF")
    with pymupdf.open(p) as doc:
        if numero < 1 or numero > len(doc):
            raise ValueError("Página fora do documento")
    return p


def visao(nome, pdf, numero):
    p = pagina(pdf, numero)
    py = Path((base / "visao-runtime.txt").read_text().strip())
    saida = base / "resultados"
    saida.mkdir(exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=saida,suffix=".json",delete=False) as f:
        destino = Path(f.name)
    try:
        subprocess.run([str(py),"-I",str(py.parents[1]/"visao.py"),nome,str(p),str(numero),str(destino)],
                       stdin=subprocess.DEVNULL,check=True,timeout=120,stdout=subprocess.DEVNULL)
        return {"pagina": numero,"arquivo": str(destino),"verificacao":"pendente"}
    except Exception:
        destino.unlink(missing_ok=True)
        raise


@server.tool()
def blueprint_pipeline(pdf: str, numero: int) -> dict:
    """Identifica regiões vetoriais candidatas e salva seus polígonos em pontos PDF."""
    return visao("blueprint",pdf,numero)


@server.tool()
def fyp_floorplan(pdf: str, numero: int) -> dict:
    """Segmenta a prancha em CPU com U-Net e salva a máscara e a legenda."""
    return visao("fyp-floorplan",pdf,numero)


@server.tool()
def pymupdf_pagina(pdf: str, numero: int) -> dict:
    """Consulta texto e dimensões da página em pontos PDF."""
    import pymupdf
    with pymupdf.open(pagina(pdf,numero)) as doc:
        pg=doc[numero-1]
        return {"pagina":numero,"texto":pg.get_text()[:12000],"dimensoes_pt":list(pg.rect)}


@server.tool()
def docling_pagina(pdf: str, numero: int) -> dict:
    """Extrai o documento estruturado de uma página com o conversor local Docling MCP."""
    import pymupdf
    from docling_mcp.tools.converters.local import LocalDocumentConverter
    from docling_mcp.shared import local_document_cache
    saida = base / "resultados"
    saida.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(dir=saida) as t, pymupdf.open(pagina(pdf,numero)) as doc:
        recorte = Path(t)/"pagina.pdf"
        with pymupdf.open() as parte:
            parte.insert_pdf(doc,from_page=numero-1,to_page=numero-1)
            parte.save(recorte)
        resultado=LocalDocumentConverter().convert_document(str(recorte))
        with tempfile.NamedTemporaryFile(mode="w",encoding="utf-8",dir=saida,suffix=".json",delete=False) as f:
            json.dump(local_document_cache[resultado.document_key].export_to_dict(),f,ensure_ascii=False)
        return {"pagina":numero,"arquivo":f.name}


if __name__ == "__main__":
    server.run(transport="stdio")
