"""Verificações de fidelidade do Editor de Relatórios PMVNI contra o gerador do kit.

Uso:  python -I tools/fidelidade.py {v1|v2|v3|v4|tudo} [--caso NOME ...]

v1    Paridade do HTML: o `Pmvni.Verificador html` e o gerador do kit (gerar_relatorio.py --rascunho --manter-html)
      produzem o mesmo HTML, byte a byte. Grava tests/saida/v1.json.
v2    Tela × PDF do editor: cada folha da tela (`Pmvni.Verificador capturas --dpi 150`, plano aplicado como na tela) contra
      a página do PDF do editor (`Pmvni.Verificador pdf`) rasterizada pelo PyMuPDF a 150 dpi; o texto de cada folha
      (`Pmvni.Verificador texto`) contra o texto da área útil da página do PDF. Grava tests/saida/v2.json.
v3    PDF do editor × PDF do gerador do kit (mesmo dados.json): mesmo número de páginas, mesmo texto em cada página
      (cabeçalho, área útil e rodapé) e diferença visual por página. Grava tests/saida/v3.json.
v4    V1, V2 e V3 sobre todos os casos (corpus + dados_modelo --instrucoes + casos locais). O caso `estouro` não tem PDF:
      verifica só que a exportação é recusada (código 3, nada gravado). Grava v1/v2/v3.json e v4.json.
tudo  O mesmo que v4.
--manter  não apaga tests/saida/tmp/<run> (investigação; recusado com casos locais).

Critérios (docs/fidelidade.md; mudar só com justificativa registrada lá):
- pixel diferente: |Δ| > 48 em tons de cinza (0–255), imagens a 150 dpi;
- V2: texto idêntico (porta principal) e ≤ 2,0 % de pixels diferentes com 1 px de tolerância de posição (um pixel só
  conta se nenhum pixel da vizinhança 3 × 3 da outra imagem estiver a |Δ| ≤ 48; nos dois sentidos, vale o maior); o estrito (sem tolerância, limite
  inicial 0,5 %) fica só informativo: mede a diferença entre os rasterizadores (Chromium na tela × MuPDF no PDF);
- V3: mesmo número de páginas, texto idêntico em cada página e ≤ 2 % de pixels diferentes por página.
- Texto: só o visível do PDF (há tinta na caixa do caractere), sem espaços em branco, sem o texto gerado por CSS
  (`content: "…"` do relatorio.css). V2: mesmos caracteres e cada trecho do PDF presente na folha (a ordem de pintura do
  PDF não é a do DOM). V3: igualdade exata por faixa (cabeçalho, área, rodapé); diferenças conhecidas contadas à parte
  e FIXADAS (outra diferença reprova): além da borda direita da área, só a etiqueta conhecida que o kit corta e o
  editor não (ROTULOS_CORTADOS_PELO_KIT); sangria na margem de baixo, no máximo 1 caractere no kit e 0 no editor. Detalhes e medidas em docs/fidelidade.md.

Regras:
- O gerador do kit é só LIDO (nada é gravado na pasta do kit; sem bytecode: python -I + dont_write_bytecode). Ele roda
  no modo padrão (abre o Edge por um .cmd via explorer.exe, fora do pacote do app).
- Tudo o que os dois lados escrevem vai para tests/saida/tmp/<run-id>/ (dentro do repositório, NUNCA em %TEMP% ou
  AppData: o Python iniciado daqui roda dentro do pacote MSIX do app Claude e escreveria em pasta virtualizada,
  que o Edge aberto via explorer.exe não enxerga). A pasta é apagada no fim.
- Casos locais (tests/local.json, ignorado pelo git) são dados reais: o nome nas saídas é só o informado, nada do
  conteúdo é impresso nem gravado; numa diferença só saem páginas, contagens e posições. tests/saida/*.json só levam
  contagens, hashes, páginas e porcentagens.
"""
from __future__ import annotations
import argparse, datetime, hashlib, json, os, re, shutil, subprocess, sys, time
from pathlib import Path

sys.dont_write_bytecode = True
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
RAIZ = Path(__file__).resolve().parent.parent
CORPUS = RAIZ / 'tests' / 'corpus'
SAIDA = RAIZ / 'tests' / 'saida'
LOCAL = RAIZ / 'tests' / 'local.json'
MODELO_OFICIAL = RAIZ / 'src' / 'Pmvni.Composicao' / 'ModeloOficial' / 'rev10'
KIT_PADRAO = (Path.home() / 'OneDrive' / 'PMVNI' / 'SIDNEY JUNIOR' / 'KIT DE TREINAMENTO DE IA (PMVNI)'
              / '02 - MODELOS DE DOCUMENTOS' / 'RELATORIO' / 'MODELO HTML (USADO PELOS PLUGINS)')
VERIFICADOR_PROJ = RAIZ / 'src' / 'Pmvni.Verificador' / 'Pmvni.Verificador.csproj'
EDGES = [Path(r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'), Path(r'C:\Program Files\Microsoft\Edge\Application\msedge.exe')]

DPI = 150
LIMIAR_PIXEL = 48          # |Δ| em tons de cinza acima do qual o pixel conta como diferente
MAX_V2 = 2.0               # % de pixels diferentes por folha (tela × PDF do editor), COM 1 px de tolerância, nos dois
                           # sentidos (decisões do controlador, 14/11/2026; ver docs/fidelidade.md): guarda grosseira
                           # para o que não é texto; a composição é provada pela identidade do texto por folha
MAX_V3 = 2.0               # % de pixels diferentes por página (editor × kit)
MM = 72 / 25.4             # pt por mm
# Faixas da página (mm, pelo centro de cada palavra): cabeçalho acima de 24 mm (a imagem termina em ~22 mm; a área útil
# começa em 28 mm), rodapé abaixo de 283,5 mm (a linha do rodapé fica em ~282,3 mm e o texto dele começa em ~284,6 mm).
FIM_CABECALHO_MM, INICIO_RODAPE_MM = 24.0, 283.5
FIM_AREA_X_MM = 196.1      # borda direita da área útil (14 + 182 mm; o recorte do kit termina em 196,0 mm)
TINTA = 230                # pixel com tinta: cinza < 230 (fundo branco 255)
MAX_FONTE_RODAPE_PT = 7.0  # o rodapé usa 6,2 pt; glifo maior na faixa do rodapé é sangria do conteúdo (contado à parte)
# Diferenças conhecidas do PDF do kit, FIXADAS (qualquer outra diferença reprova a V3; docs/fidelidade.md):
# - o kit corta, na borda direita da área, a etiqueta (nowrap) abaixo; o editor a mostra inteira: cada trecho do editor
#   com caracteres além da borda tem de ser exatamente esta etiqueta, e a parte além da borda, um fim dela;
ROTULOS_CORTADOS_PELO_KIT = ('INFORMAÇÕESDIVERGENTES',)
# - o kit deixa na margem de baixo ~1 px do acento do 1º glifo da página seguinte: no máximo 1 caractere por página
#   no kit, nenhum no editor.
MAX_SANGRIA_KIT, MAX_SANGRIA_EDITOR = 1, 0
CASO_RECUSA = 'estouro'    # bloco maior que a página: o editor recusa a exportação
# Pasta de dados do editor (perfil do WebView2, sessões) dentro do repositório, como nos testes: fora do AppData virtualizado.
os.environ.setdefault('PMVNI_DADOS_DIR', str(SAIDA / 'dados'))


def sha(b: bytes) -> str:
    return hashlib.sha256(b).hexdigest()


def pasta_do_kit() -> Path:
    p = Path(os.environ.get('PMVNI_KIT_DIR') or KIT_PADRAO)
    if not (p / 'gerar_relatorio.py').is_file():
        raise SystemExit(f'Python ou kit ausente: gerar_relatorio.py não encontrado em {p} (defina PMVNI_KIT_DIR)')
    return p


def compilar_verificador() -> Path:
    r = subprocess.run(['dotnet', 'build', str(VERIFICADOR_PROJ), '-c', 'Release', '--nologo', '-v', 'q'],
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    if r.returncode != 0:
        raise SystemExit('Falha ao compilar o Pmvni.Verificador:\n' + r.stdout[-2000:] + r.stderr[-2000:])
    exes = sorted((VERIFICADOR_PROJ.parent / 'bin' / 'Release').rglob('Pmvni.Verificador.exe'), key=lambda p: p.stat().st_mtime, reverse=True)
    if not exes:
        raise SystemExit('Pmvni.Verificador.exe não encontrado após a compilação')
    return exes[0]


def casos(filtro: list[str] | None = None) -> list[dict]:
    """Lista de {nome, dados, instrucoes, local}."""
    lista = [{'nome': p.stem, 'dados': p, 'instrucoes': False, 'local': False} for p in sorted(CORPUS.glob('*.json'))]
    lista.append({'nome': 'dados_modelo', 'dados': MODELO_OFICIAL / 'dados_modelo.json', 'instrucoes': True, 'local': False})
    if LOCAL.is_file():
        for c in json.loads(LOCAL.read_text(encoding='utf-8-sig')).get('casos_locais', []):
            lista.append({'nome': str(c['nome']), 'dados': Path(c['dados']), 'instrucoes': False, 'local': True})
    if filtro:
        desconhecidos = set(filtro) - {c['nome'] for c in lista}
        if desconhecidos:
            raise SystemExit('Caso(s) desconhecido(s): ' + ', '.join(sorted(desconhecidos)))
        lista = [c for c in lista if c['nome'] in filtro]
    return lista


def primeira_diferenca(a: bytes, b: bytes) -> str:
    n = min(len(a), len(b))
    pos = next((i for i in range(n) if a[i] != b[i]), n)
    return f'kit={len(a)} bytes, editor={len(b)} bytes, primeiro byte diferente na posição {pos}'


def apagar(pasta: Path):
    for _ in range(20):
        try:
            shutil.rmtree(pasta)
            return
        except FileNotFoundError:
            return
        except OSError:
            time.sleep(2)
    print(f'AVISO: não consegui apagar {pasta}; apague manualmente.', file=sys.stderr)


def versao_edge() -> str | None:
    """Versão do arquivo msedge.exe que o gerador do kit usa (o primeiro de EDGES que existe, como no kit)."""
    edge = next((p for p in EDGES if p.is_file()), None)
    if edge is None:
        return None
    r = subprocess.run(['powershell', '-NoProfile', '-Command', f"(Get-Item -LiteralPath '{edge}').VersionInfo.FileVersion"],
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    return r.stdout.strip() or None


def textos_gerados_por_css() -> list[str]:
    """Textos `content: "…"` não vazios do relatorio.css do modelo (pseudo-elementos): estão no PDF, não no innerText."""
    css = (MODELO_OFICIAL / 'relatorio.css').read_text(encoding='utf-8')
    return [sem_espacos(t) for t in re.findall(r'content:\s*"([^"]+)"', css) if sem_espacos(t)]


def sem_espacos(s: str) -> str:
    return ''.join(s.split())


def primeira_diferenca_texto(a: str, b: str) -> str:
    """Só contagens e a posição (nunca o texto: pode ser de um caso local)."""
    n = min(len(a), len(b))
    pos = next((i for i in range(n) if a[i] != b[i]), n)
    return f'{len(a)} × {len(b)} caracteres, primeira diferença na posição {pos}'


class Execucao:
    """Uma rodada: pasta temporária, Verificador compilado e o que já foi gerado para cada caso (kit, PDF do editor)."""

    def __init__(self, precisa_kit: bool):
        self.kit = pasta_do_kit() if precisa_kit else None
        self.exe = compilar_verificador()
        self.run = SAIDA / 'tmp' / ('fid-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
        self.run.mkdir(parents=True, exist_ok=True)
        self.cache: dict[tuple[str, str], object] = {}
        self.css_gerado = textos_gerados_por_css()
        self.webview2: str | None = None
        self.edge: str | None = versao_edge() if precisa_kit else None

    def fechar(self, manter: bool = False):
        if manter:
            print(f'(--manter) temporários mantidos em {self.run}; apague a pasta depois de investigar.')
            return
        apagar(self.run)
        try:
            (SAIDA / 'tmp').rmdir()
        except OSError:
            pass

    def pasta(self, c: dict) -> Path:
        p = self.run / c['nome']
        p.mkdir(parents=True, exist_ok=True)
        return p

    def _flags(self, c: dict) -> list[str]:
        return ['--rascunho'] + (['--instrucoes'] if c['instrucoes'] else [])

    def verificador(self, c: dict, comando: str, saida: Path, *extra: str) -> subprocess.CompletedProcess:
        return subprocess.run([str(self.exe), comando, str(c['dados']), str(saida), *self._flags(c), *extra],
                              capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=900)

    def falha(self, c: dict, quem: str, r: subprocess.CompletedProcess) -> str:
        return f'{quem} falhou (código {r.returncode})' + ('' if c['local'] else ': ' + (r.stderr or r.stdout)[-600:])

    def pdf_kit(self, c: dict) -> Path:
        """PDF e HTML do gerador do kit (uma vez por caso e rodada), no modo padrão (Edge via explorer.exe)."""
        chave = (c['nome'], 'kit')
        if chave not in self.cache:
            pdf = self.pasta(c) / 'kit.pdf'
            r = subprocess.run([sys.executable, '-I', str(self.kit / 'gerar_relatorio.py'), str(c['dados']), str(pdf), '--manter-html',
                                *self._flags(c)], capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=600)
            if r.returncode != 0 or not pdf.is_file() or not pdf.with_suffix('.html').is_file():
                raise RuntimeError(self.falha(c, 'kit', r))
            self.cache[chave] = pdf
        return self.cache[chave]

    def pdf_editor(self, c: dict) -> tuple[Path | None, int]:
        """(PDF do editor ou None se recusado, código de saída). Código 3 = exportação recusada (estouro)."""
        chave = (c['nome'], 'pdf')
        if chave not in self.cache:
            pdf = self.pasta(c) / 'editor.pdf'
            r = self.verificador(c, 'pdf', pdf)
            if r.returncode == 3:
                self.cache[chave] = (None, 3)
            elif r.returncode != 0 or not pdf.is_file():
                raise RuntimeError(self.falha(c, 'Pmvni.Verificador pdf', r))
            else:
                recibo = json.loads(pdf.with_suffix('.recibo-editor.json').read_text(encoding='utf-8'))
                self.webview2 = recibo.get('webview2')
                self.cache[chave] = (pdf, 0)
        return self.cache[chave]


# ───────────────────────── PDF e imagens (PyMuPDF + numpy) ─────────────────────────
def _fitz():
    import warnings
    with warnings.catch_warnings():
        warnings.simplefilter('ignore')
        import pymupdf
    return pymupdf


def paginas_pdf(pdf: Path) -> int:
    with _fitz().open(pdf) as d:
        return d.page_count


def texto_paginas(pdf: Path, css_gerado: list[str], raster: list) -> list[dict]:
    """Texto VISÍVEL de cada página, sem espaços: {'cabecalho', 'area', 'rodape', 'trechos', 'fora'}.

    Visível = há tinta (pixel < TINTA) dentro da caixa do caractere na página rasterizada (`raster`, a DPI). O PDF do
    kit grava glifos que o próprio Chromium recorta (o recorte da área útil, [14; 196] × [28; 282] mm): o acento de um
    "Á" da página seguinte na margem de baixo, o fim de uma etiqueta "INFORMAÇÕES DIVERGENTES" além da borda direita;
    a extração do PyMuPDF não respeita esse recorte (TEXT_CLIP não o vê), a tinta sim.
    Faixas pelo centro do caractere: cabeçalho (y < FIM_CABECALHO_MM), rodapé (y > INICIO_RODAPE_MM), área (o resto).
    'sangria': caracteres visíveis na faixa do rodapé com fonte maior que MAX_FONTE_RODAPE_PT (conteúdo, não rodapé: o
    Chromium do kit deixa na margem de baixo um fragmento, de ~1 px, do acento do 1º glifo da página seguinte).
    'fora_trechos': [(texto do trecho inteiro, parte além da borda)] dos trechos com caracteres além da borda;
    'fora_txt': essas partes, juntas.
    'fora': caracteres visíveis da faixa da área com o centro à direita de FIM_AREA_X_MM (além da borda direita da área
    útil, onde o kit corta e o editor não; ver docs/fidelidade.md) — ficam fora de 'area' e são contados à parte.
    A área segue a ordem do conteúdo do PDF (ordem de pintura do Chromium); cabeçalho e rodapé, a posição (as caixas
    de margem do kit são pintadas em outra ordem que as do editor). 'trechos': o texto de cada trecho (span) da área.
    Os textos gerados por CSS saem da área e dos trechos."""
    fz = _fitz()
    esc = DPI / 72
    flags = fz.TEXT_PRESERVE_LIGATURES | fz.TEXT_PRESERVE_WHITESPACE | fz.TEXT_MEDIABOX_CLIP
    res = []
    with fz.open(pdf) as d:
        for p, img in zip(d, raster):
            h, w = img.shape

            def visivel(b) -> bool:
                x0, y0 = max(0, int(b[0] * esc)), max(0, int(b[1] * esc))
                x1, y1 = min(w, int(b[2] * esc + 1)), min(h, int(b[3] * esc + 1))
                return x1 > x0 and y1 > y0 and bool((img[y0:y1, x0:x1] < TINTA).any())

            cab, area, rod, todos, sangria, fora_trechos = [], [], [], [], 0, []
            for b in p.get_text('rawdict', flags=flags)['blocks']:
                for l in b.get('lines', []):
                    for sp in l['spans']:
                        t_cab, t_area, t_rod, t_fora = [], [], [], []
                        for ch in sp['chars']:
                            c = ch['c']
                            if not c.strip() or not visivel(ch['bbox']):
                                continue
                            x0, y0, x1, y1 = ch['bbox']
                            xm, ym = (x0 + x1) / 2 / MM, (y0 + y1) / 2 / MM
                            if ym < FIM_CABECALHO_MM:
                                t_cab.append(c)
                            elif ym > INICIO_RODAPE_MM:
                                if sp['size'] > MAX_FONTE_RODAPE_PT:
                                    sangria += 1  # glifo do conteúdo (ex.: título da página seguinte) na margem de baixo
                                else:
                                    t_rod.append(c)
                            elif xm > FIM_AREA_X_MM:
                                t_fora.append(c)
                            else:
                                t_area.append(c)
                        y0, x0 = round(sp['bbox'][1], 1), sp['bbox'][0]
                        if t_cab:
                            cab.append((y0, x0, ''.join(t_cab)))
                        if t_rod:
                            rod.append((y0, x0, ''.join(t_rod)))
                        t = ''.join(t_area)
                        if t:
                            area.append(t)
                        if t or t_fora:
                            todos.append(t + ''.join(t_fora))
                        if t_fora:
                            fora_trechos.append((t + ''.join(t_fora), ''.join(t_fora)))

            def sem_gerado(x: str) -> str:
                for g in css_gerado:
                    x = x.replace(g, '')
                return x

            a, a_toda = sem_gerado(''.join(area)), sem_gerado(''.join(todos))
            res.append({'cabecalho': ''.join(t for *_, t in sorted(cab)), 'area': a, 'area_toda': a_toda,
                        'fora': len(a_toda) - len(a), 'fora_trechos': fora_trechos,
                        'fora_txt': ''.join(f for _, f in fora_trechos), 'rodape': ''.join(t for *_, t in sorted(rod)), 'sangria': sangria,
                        # o gerado por CSS pode vir em trechos separados (o "✎" sai em outra fonte): fora da conferência
                        'trechos': [t for t in todos if not any(t in g for g in css_gerado)]})
    return res


def comparar_texto_tela(tela: str, pagina: dict) -> str | None:
    """V2: texto da folha na tela (innerText, ordem do DOM) × texto da página do PDF (ordem de pintura: o Chromium pinta
    os blocos posicionados, como .card, depois do fluxo normal, logo a ordem difere). Iguais quando (1) os caracteres
    são os mesmos, com as mesmas contagens, e (2) cada trecho do PDF aparece, inteiro, no texto da folha. None = iguais;
    senão o motivo, só com contagens e posições."""
    a, b = sem_espacos(tela), pagina['area_toda']
    if sorted(a) != sorted(b):
        return 'caracteres diferentes: ' + primeira_diferenca_texto(''.join(sorted(a)), ''.join(sorted(b))) + ' (ordenados)'
    fora = [i for i, t in enumerate(pagina['trechos']) if t not in a]
    if fora:
        return f'{len(fora)} de {len(pagina["trechos"])} trechos do PDF não aparecem na folha (1º: trecho {fora[0]}, {len(pagina["trechos"][fora[0]])} caracteres)'
    return None


def raster_pdf(pdf: Path):
    """Páginas do PDF em tons de cinza a DPI (numpy uint8, altura × largura)."""
    import numpy as np
    fz = _fitz()
    out = []
    with fz.open(pdf) as d:
        for p in d:
            pix = p.get_pixmap(dpi=DPI, colorspace=fz.csGRAY, alpha=False)
            out.append(np.frombuffer(pix.samples, dtype=np.uint8).reshape(pix.height, pix.stride)[:, :pix.width].copy())
    return out


def ler_png_cinza(png: Path):
    import numpy as np
    fz = _fitz()
    pix = fz.Pixmap(str(png))
    if pix.alpha:
        pix = fz.Pixmap(pix, 0)
    if pix.n != 1:
        pix = fz.Pixmap(fz.csGRAY, pix)
    return np.frombuffer(pix.samples, dtype=np.uint8).reshape(pix.height, pix.stride)[:, :pix.width].copy()


def diferenca_visual(a, b) -> tuple[float | None, str | None]:
    """% de pixels com |Δ| > LIMIAR_PIXEL. Tamanhos podem diferir em até 1 px (arredondamento de 209,889 mm a 150 dpi):
    compara a parte comum. Mais que isso: None e o motivo."""
    import numpy as np
    (ha, la), (hb, lb) = a.shape, b.shape
    if abs(ha - hb) > 1 or abs(la - lb) > 1:
        return None, f'tamanhos diferentes: {la}×{ha} e {lb}×{hb} px'
    h, l = min(ha, hb), min(la, lb)
    d = np.abs(a[:h, :l].astype(np.int16) - b[:h, :l].astype(np.int16)) > LIMIAR_PIXEL
    return float(d.sum()) * 100.0 / (h * l), None


def _tolerante_um_lado(x, y) -> float:
    """% de pixels de x sem nenhum pixel de y na vizinhança 3 × 3 com |Δ| ≤ LIMIAR_PIXEL (x, y do mesmo tamanho)."""
    import numpy as np
    h, l = x.shape
    yp = np.pad(y, 1, mode='edge')
    melhor = np.full(x.shape, 999, dtype=np.int16)
    for dy in (0, 1, 2):
        for dx in (0, 1, 2):
            melhor = np.minimum(melhor, np.abs(x - yp[dy:dy + h, dx:dx + l]))
    return float((melhor > LIMIAR_PIXEL).sum()) * 100.0 / (h * l)


def diferenca_tolerante(a, b) -> float | None:
    """Critério visual da V2, simétrico: o maior entre (% de pixels de `a` sem nenhum pixel de `b` na vizinhança 3 × 3
    com |Δ| ≤ LIMIAR_PIXEL) e o mesmo de `b` contra `a`. Assim pega tanto tinta da tela que falta no PDF quanto tinta do
    PDF que falta na tela. Separa a diferença de rasterização do texto (Chromium na tela × MuPDF no PDF: bordas dos
    glifos, deslocadas em até 1 px) de diferença de composição (algo fora do lugar, a mais ou a menos)."""
    import numpy as np
    (ha, la), (hb, lb) = a.shape, b.shape
    if abs(ha - hb) > 1 or abs(la - lb) > 1:
        return None
    h, l = min(ha, hb), min(la, lb)
    x, y = a[:h, :l].astype(np.int16), b[:h, :l].astype(np.int16)
    return max(_tolerante_um_lado(x, y), _tolerante_um_lado(y, x))


# ───────────────────────── V1 ─────────────────────────
def caso_v1(ex: Execucao, c: dict) -> dict:
    res = {'caso': c['nome'], 'igual': False, 'sha256_kit': None, 'sha256_editor': None}
    html_kit = ex.pdf_kit(c).with_suffix('.html')
    html_ed = ex.pasta(c) / 'editor.html'
    r = ex.verificador(c, 'html', html_ed)
    if r.returncode != 0 or not html_ed.is_file():
        raise RuntimeError(ex.falha(c, 'Pmvni.Verificador html', r))
    a, b = html_kit.read_bytes(), html_ed.read_bytes()
    res.update(sha256_kit=sha(a), sha256_editor=sha(b), igual=(a == b))
    print(f'  V1 [{c["nome"]}] ' + ('IGUAL' if res['igual'] else 'DIFERE: ' + primeira_diferenca(a, b)) + f' ({len(a)} bytes)')
    return res


# ───────────────────────── V2 ─────────────────────────
def caso_v2(ex: Execucao, c: dict) -> dict:
    nome = c['nome']
    res = {'caso': nome, 'aprovado': False, 'folhas': None, 'paginas_pdf': None, 'recusado': None,
           'texto_divergente': [], 'visual_max_pct': None, 'visual_pct': [], 'visual_reprovadas': [],
           'visual_estrito_max_pct': None, 'visual_estrito_pct': []}
    pdf, codigo = ex.pdf_editor(c)
    if nome == CASO_RECUSA:
        res['recusado'] = codigo == 3 and not (ex.pasta(c) / 'editor.pdf').exists()
        res['aprovado'] = res['recusado']
        print(f'  V2 [{nome}] exportação ' + ('recusada, nada gravado (OK)' if res['aprovado'] else 'NÃO recusada'))
        return res
    if pdf is None:
        print(f'  V2 [{nome}] exportação recusada (estouro) — inesperado')
        res['recusado'] = True
        return res
    tj = ex.pasta(c) / 'texto.json'
    r = ex.verificador(c, 'texto', tj)
    if r.returncode != 0:
        raise RuntimeError(ex.falha(c, 'Pmvni.Verificador texto', r))
    tela = json.loads(tj.read_text(encoding='utf-8'))
    cap = ex.pasta(c) / 'capturas'
    r = ex.verificador(c, 'capturas', cap, '--dpi', str(DPI))
    if r.returncode != 0:
        raise RuntimeError(ex.falha(c, 'Pmvni.Verificador capturas', r))
    pngs = sorted(cap.glob('folha-*.png'))
    rast = raster_pdf(pdf)
    paginas = texto_paginas(pdf, ex.css_gerado, rast)
    res.update(folhas=tela['folhas'], paginas_pdf=len(paginas))
    ok = tela['folhas'] == len(paginas) == len(tela['textos']) == len(pngs)
    if not ok:
        print(f'  V2 [{nome}] contagem: {tela["folhas"]} folhas, {len(tela["textos"])} textos, {len(pngs)} capturas, {len(paginas)} páginas')
    for i, (t, p) in enumerate(zip(tela['textos'], paginas), start=1):
        motivo = comparar_texto_tela(t, p)
        if motivo:
            res['texto_divergente'].append(i)
            print(f'  V2 [{nome}] folha {i}: texto difere (tela × PDF): {motivo}')
    for i, (png, pg) in enumerate(zip(pngs, rast), start=1):
        cap = ler_png_cinza(png)
        estrito, motivo = diferenca_visual(cap, pg)
        tol = diferenca_tolerante(cap, pg)
        if tol is None:
            print(f'  V2 [{nome}] folha {i}: {motivo}')
            res['visual_reprovadas'].append(i)
            res['visual_pct'].append(None)
            res['visual_estrito_pct'].append(None)
            continue
        res['visual_pct'].append(round(tol, 4))
        res['visual_estrito_pct'].append(round(estrito, 4))
        if tol > MAX_V2:
            res['visual_reprovadas'].append(i)
    vals = [v for v in res['visual_pct'] if v is not None]
    res['visual_max_pct'] = max(vals) if vals else None
    estritos = [v for v in res['visual_estrito_pct'] if v is not None]
    res['visual_estrito_max_pct'] = max(estritos) if estritos else None
    res['aprovado'] = ok and not res['texto_divergente'] and not res['visual_reprovadas']
    print(f'  V2 [{nome}] {"OK" if res["aprovado"] else "FALHOU"}: {res["folhas"]} folhas; texto divergente em {res["texto_divergente"] or "nenhuma"};'
          f' visual (1 px de tolerância) máx {res["visual_max_pct"]} % (limite {MAX_V2} %), acima em {res["visual_reprovadas"] or "nenhuma"};'
          f' informativo, estrito: máx {res["visual_estrito_max_pct"]} %')
    return res


# ───────────────────────── V3 ─────────────────────────
def conferir_conhecidas(kit: dict, editor: dict) -> list[str]:
    """V3: as diferenças conhecidas (fora da área, sangria) dentro do que foi fixado; devolve os motivos (só contagens)."""
    motivos = []
    if kit['fora_txt'] not in editor['fora_txt']:
        motivos.append(f'além da borda: o texto do kit ({len(kit["fora_txt"])} caracteres) não está no do editor ({len(editor["fora_txt"])})')
    estranhos = [(len(t), len(f)) for t, f in editor['fora_trechos']
                 if t not in ROTULOS_CORTADOS_PELO_KIT or not t.endswith(f) or f == t]
    if estranhos:
        motivos.append(f'além da borda: {len(estranhos)} trecho(s) do editor fora da etiqueta conhecida (tamanho, além da borda: {estranhos})')
    if kit['sangria'] > MAX_SANGRIA_KIT or editor['sangria'] > MAX_SANGRIA_EDITOR:
        motivos.append(f'sangria na margem de baixo acima do conhecido: kit {kit["sangria"]} (máx. {MAX_SANGRIA_KIT}),'
                       f' editor {editor["sangria"]} (máx. {MAX_SANGRIA_EDITOR})')
    return motivos


def caso_v3(ex: Execucao, c: dict) -> dict:
    nome = c['nome']
    res = {'caso': nome, 'aprovado': False, 'paginas_kit': None, 'paginas_editor': None, 'recusado': None,
           'texto_divergente': [], 'visual_max_pct': None, 'visual_pct': [], 'visual_reprovadas': [],
           'fora_da_area': [], 'sangria_rodape': []}
    kit = ex.pdf_kit(c)
    res['paginas_kit'] = paginas_pdf(kit)
    pdf, codigo = ex.pdf_editor(c)
    if nome == CASO_RECUSA:
        res['recusado'] = codigo == 3
        res['aprovado'] = res['recusado']
        print(f'  V3 [{nome}] kit {res["paginas_kit"]} páginas; editor ' + ('recusou a exportação (OK)' if res['aprovado'] else 'NÃO recusou'))
        return res
    if pdf is None:
        res['recusado'] = True
        print(f'  V3 [{nome}] exportação recusada (estouro) — inesperado')
        return res
    rk, re_ = raster_pdf(kit), raster_pdf(pdf)
    tk, te = texto_paginas(kit, ex.css_gerado, rk), texto_paginas(pdf, ex.css_gerado, re_)
    res['paginas_editor'] = len(te)
    for i, (a, b) in enumerate(zip(tk, te), start=1):
        # Diferenças conhecidas, contadas à parte (docs/fidelidade.md): [página, kit, editor].
        if a['fora'] or b['fora']:
            res['fora_da_area'].append([i, a['fora'], b['fora']])
            print(f'  V3 [{nome}] página {i}: além da borda direita da área: kit {a["fora"]}, editor {b["fora"]} caracteres visíveis')
        if a['sangria'] or b['sangria']:
            res['sangria_rodape'].append([i, a['sangria'], b['sangria']])
            print(f'  V3 [{nome}] página {i}: sangria na margem de baixo: kit {a["sangria"]}, editor {b["sangria"]} caracteres')
        partes = [k for k in ('cabecalho', 'area', 'rodape') if a[k] != b[k]]
        conhecidas = conferir_conhecidas(a, b)
        if partes or conhecidas:
            res['texto_divergente'].append(i)
        if partes:
            print(f'  V3 [{nome}] página {i}: texto difere em ' + ', '.join(
                f'{k} ({primeira_diferenca_texto(a[k], b[k])}; kit × editor)' for k in partes))
        for m in conhecidas:
            print(f'  V3 [{nome}] página {i}: diferença fora do conhecido — {m}')
    for i, (ra, rb) in enumerate(zip(rk, re_), start=1):
        pct, motivo = diferenca_visual(ra, rb)
        if pct is None:
            print(f'  V3 [{nome}] página {i}: {motivo}')
            res['visual_reprovadas'].append(i)
            res['visual_pct'].append(None)
            continue
        res['visual_pct'].append(round(pct, 4))
        if pct > MAX_V3:
            res['visual_reprovadas'].append(i)
    vals = [v for v in res['visual_pct'] if v is not None]
    res['visual_max_pct'] = max(vals) if vals else None
    mesmas = res['paginas_kit'] == res['paginas_editor']
    res['aprovado'] = mesmas and not res['texto_divergente'] and not res['visual_reprovadas']
    print(f'  V3 [{nome}] {"OK" if res["aprovado"] else "FALHOU"}: páginas kit {res["paginas_kit"]} × editor {res["paginas_editor"]};'
          f' texto divergente em {res["texto_divergente"] or "nenhuma"}; visual máx {res["visual_max_pct"]} % (limite {MAX_V3} %),'
          f' acima em {res["visual_reprovadas"] or "nenhuma"}')
    return res


VERIFICACOES = {'v1': caso_v1, 'v2': caso_v2, 'v3': caso_v3}
VAZIO = {'v1': lambda n: {'caso': n, 'igual': False, 'sha256_kit': None, 'sha256_editor': None},
         'v2': lambda n: {'caso': n, 'aprovado': False, 'erro': True},
         'v3': lambda n: {'caso': n, 'aprovado': False, 'erro': True}}


def aprovado(v: str, r: dict) -> bool:
    return bool(r.get('igual') if v == 'v1' else r.get('aprovado'))


def gravar(nome: str, conteudo) -> None:
    SAIDA.mkdir(parents=True, exist_ok=True)
    (SAIDA / nome).write_text(json.dumps(conteudo, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')


def executar(quais: list[str], filtro: list[str] | None, consolidado: bool, manter: bool = False) -> int:
    lista = casos(filtro)
    ex = Execucao(precisa_kit=any(v in quais for v in ('v1', 'v3')))
    resultados: dict[str, list[dict]] = {v: [] for v in quais}
    try:
        for c in lista:
            for v in quais:
                try:
                    resultados[v].append(VERIFICACOES[v](ex, c))
                except Exception as e:  # um caso com problema não esconde os outros
                    print(f'  {v.upper()} [{c["nome"]}] erro: {type(e).__name__}' + ('' if c['local'] else f': {e}'))
                    resultados[v].append(VAZIO[v](c['nome']))
    finally:
        ex.fechar(manter)
    ok = True
    for v in quais:
        gravar(f'{v}.json', resultados[v])
        n = sum(aprovado(v, r) for r in resultados[v])
        ok &= n == len(resultados[v])
        print(f'{v.upper()} ' + ('OK' if n == len(resultados[v]) else 'FALHOU') + f': {n}/{len(resultados[v])} casos aprovados')
    if consolidado:
        gravar('v4.json', {
            'gerado_em': datetime.datetime.now().isoformat(timespec='seconds'),
            'versoes': {'webview2_editor': ex.webview2, 'edge_kit': ex.edge},
            'criterios': {'dpi': DPI, 'limiar_pixel': LIMIAR_PIXEL, 'v2_max_pct_tolerancia_1px': MAX_V2, 'v3_max_pct': MAX_V3},
            'casos': [{'caso': c['nome'], **{v: aprovado(v, next(r for r in resultados[v] if r['caso'] == c['nome'])) for v in quais}}
                      for c in lista],
            'aprovado': ok,
        })
        print(('V4 OK' if ok else 'V4 FALHOU') + f' (WebView2 do editor {ex.webview2}; Edge do kit {ex.edge})')
    return 0 if ok else 1


def main() -> int:
    ap = argparse.ArgumentParser(description='Verificações de fidelidade do Editor de Relatórios PMVNI')
    ap.add_argument('cmd', choices=['v1', 'v2', 'v3', 'v4', 'tudo'])
    ap.add_argument('--caso', action='append', help='só este caso (pode repetir)')
    ap.add_argument('--manter', action='store_true', help='não apaga tests/saida/tmp/<run> (investigação; só casos do corpus)')
    a = ap.parse_args()
    if a.manter and any(c['local'] for c in casos(a.caso)):
        raise SystemExit('--manter não vale com casos locais (dados reais): use --caso com casos do corpus.')
    if a.cmd in ('v4', 'tudo'):
        return executar(['v1', 'v2', 'v3'], a.caso, consolidado=True, manter=a.manter)
    return executar([a.cmd], a.caso, consolidado=False, manter=a.manter)


if __name__ == '__main__':
    sys.exit(main())
