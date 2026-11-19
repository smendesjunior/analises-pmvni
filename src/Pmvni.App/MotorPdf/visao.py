"""Motores de visão locais, executados no ambiente de dependências de visão."""
import hashlib
import json
import os
import sys
from pathlib import Path

import pymupdf


def inferir(nome, doc, pagina, fontes):
    pg = doc[pagina - 1]
    if nome == "blueprint":
        sys.path.insert(0, str(next(fontes.glob("blueprint_pipeline-*/src")).parent))
        from src.vector.extractor import extract_wall_segments_simple
        from src.vector.polygonizer import segments_to_polygons
        segmentos = extract_wall_segments_simple(pg)
        regioes, _ = segments_to_polygons(segmentos, pg.rect.width, pg.rect.height)
        return {"unidade": "pt", "segmentos": len(segmentos), "regioes": [r.vertices for r in regioes]}
    import torch
    import numpy as np
    import segmentation_models_pytorch as smp
    from PIL import Image
    torch.set_num_threads(min(4, os.cpu_count() or 1))
    model = smp.Unet(encoder_name="resnet34", encoder_weights=None, classes=5)
    model.load_state_dict(torch.load(fontes / "fyp-unet.pth", weights_only=True, map_location="cpu"))
    model.eval()
    pix = pg.get_pixmap(matrix=pymupdf.Matrix(1024/max(pg.rect),1024/max(pg.rect)), colorspace=pymupdf.csRGB, alpha=False)
    img = Image.frombytes("RGB",(pix.width,pix.height),pix.samples).resize((512,512))
    x = np.asarray(img,dtype=np.float32)/255
    x = (x-np.array([.485,.456,.406],dtype=np.float32))/np.array([.229,.224,.225],dtype=np.float32)
    with torch.inference_mode():
        mask = model(torch.from_numpy(x.transpose(2,0,1)).unsqueeze(0)).argmax(1)[0].byte().numpy()
    return {"classes": ["fundo","parede","ambiente","porta","janela"], "dimensoes_mascara":[512,512],
            "mascara_png_base64": codificar(mask)}


def codificar(mask):
    from PIL import Image
    import io, base64
    stream = io.BytesIO()
    Image.fromarray(mask).save(stream,format="PNG")
    return base64.b64encode(stream.getvalue()).decode("ascii")


if __name__ == "__main__":
    nome, arquivo, numero, saida = sys.argv[1:]
    fontes = Path(os.environ["LOCALAPPDATA"]) / "PMVNI/MotorPdf/fontes"
    with pymupdf.open(arquivo) as doc:
        dados = inferir(nome, doc, int(numero), fontes)
    with open(arquivo,"rb") as entrada:
        sha = hashlib.file_digest(entrada,"sha256").hexdigest()
    Path(saida).write_text(json.dumps({"pagina":int(numero),"sha256":sha,"verificacao":"pendente",**dados}),encoding="utf-8")
