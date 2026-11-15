"""Gera o corpus fictício de testes (tests/corpus): dados.json de relatório e as imagens (PIL) que eles citam.

Uso:  python -I tools/gerar_corpus.py

Determinístico: semente fixa e nenhum dado externo; rodar de novo reproduz os mesmos arquivos.
Todos os nomes, endereços, números e textos são inventados (nada vem de processo real).
Casos: curto, longo, extremos, estouro, sem_opcionais (usados por fidelidade.py v1 e pelos testes de paginação).
"""
from __future__ import annotations
import json, random
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

RAIZ = Path(__file__).resolve().parent.parent
SAIDA = RAIZ / 'tests' / 'corpus'
SEMENTE = 20261114

ITEM_SIT = ['ATENDE', 'NAO_ATENDE', 'NAO_COMPROVADO', 'DIVERGENCIA_INTERNA']
IND_SIT = ['CONTA_CONFERE', 'NAO_ATENDE', 'PENDENTE', 'DIVERGENCIA']
ICONES_IND = ['plot', 'to', 'ca', 'tp', 'pav', 'afa']
ICONES_AMB = ['bed', 'wc', 'pot', 'wash', 'sun']

PALAVRAS = ('projeto planta corte fachada cota medida área vão janela porta ambiente terreno lote testada afastamento recuo '
            'divisa passeio calçada muro beiral cobertura telhado laje pavimento escada rampa garagem vaga circulação '
            'ventilação iluminação permeável edificação existente proposta indicada declarada apresentada conferida '
            'quadro prancha legenda escala carimbo memorial matrícula certidão alvará licença norma artigo parágrafo '
            'exigência mínimo máximo coeficiente taxa ocupação aproveitamento limite zona parâmetro cálculo soma '
            'diferença confere diverge falta informar apresentar corrigir indicar cotar complementar').split()


def frase(rng: random.Random, n: int = 11) -> str:
    p = [rng.choice(PALAVRAS) for _ in range(rng.randint(max(4, n - 3), n + 3))]
    p[0] = p[0].capitalize()
    return ' '.join(p) + '.'


def texto(rng: random.Random, frases: int = 3, n: int = 11) -> str:
    return ' '.join(frase(rng, n) for _ in range(frases))


# ───────────────────────── imagens ─────────────────────────
def fonte(tam: int):
    for nome in ('arial.ttf', 'DejaVuSans.ttf', 'verdana.ttf'):
        try:
            return ImageFont.truetype(nome, tam)
        except OSError:
            continue
    return ImageFont.load_default()


def imagem(nome: str, w: int, h: int, rotulo: str, semente: int, mapa: bool = False):
    """Desenho esquemático fictício (grade + retângulos + marcas numeradas), em paleta reduzida para ficar leve."""
    rng = random.Random(semente)
    im = Image.new('RGB', (w, h), (246, 248, 250))
    dr = ImageDraw.Draw(im)
    passo = max(40, w // 20)
    for x in range(0, w, passo): dr.line([(x, 0), (x, h)], fill=(221, 228, 235), width=1)
    for y in range(0, h, passo): dr.line([(0, y), (w, y)], fill=(221, 228, 235), width=1)
    if mapa:
        dr.rectangle([w * .30, h * .25, w * .68, h * .75], outline=(36, 112, 179), width=max(3, w // 300))
        dr.rectangle([w * .38, h * .38, w * .56, h * .60], fill=(190, 205, 220), outline=(29, 61, 94), width=2)
        dr.line([(0, h * .85), (w, h * .70)], fill=(47, 158, 98), width=max(4, w // 200))
    else:
        for _ in range(7):
            x0, y0 = rng.randint(0, int(w * .6)), rng.randint(0, int(h * .6))
            x1, y1 = x0 + rng.randint(int(w * .1), int(w * .35)), y0 + rng.randint(int(h * .08), int(h * .3))
            dr.rectangle([x0, y0, min(x1, w - 2), min(y1, h - 2)], outline=(29, 61, 94), width=2)
        for k in range(1, 5):
            cx, cy = rng.randint(40, w - 40), rng.randint(40, h - 40)
            r = max(14, min(w, h) // 40)
            dr.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(200, 40, 40))
            dr.text((cx - r // 3, cy - r // 2), str(k), fill=(255, 255, 255), font=fonte(r))
    dr.text((12, 10), rotulo, fill=(29, 61, 94), font=fonte(max(16, w // 40)))
    im = im.quantize(colors=24, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    dest = SAIDA / 'img' / nome
    dest.parent.mkdir(parents=True, exist_ok=True)
    im.save(dest, format='PNG', optimize=True)


# ───────────────────────── blocos de dados ─────────────────────────
def capa(rng, proc='9001/2026', requerente='CONDOMÍNIO RESIDENCIAL EXEMPLO ALFA'):
    return {
        'protocolo': '03/02/2026', 'rotulo_data': 'ANÁLISE', 'data': '14/11/2026', 'processo': proc, 'requerente': requerente,
        'quadros': [
            {'titulo': 'LOCALIZAÇÃO', 'cor': 'g', 'destaque': 'Rua das Palmeiras Imaginárias, nº 100, Bairro Exemplo', 'apoio': 'Acesso pela via principal'},
            {'titulo': 'CARACTERIZAÇÃO', 'cor': 'y', 'destaque': 'Residencial multifamiliar', 'apoio': '8 unidades • 3 pavimentos'},
            {'titulo': 'AUTORIA INDICADA', 'cor': 'y', 'destaque': 'Fulana de Tal Exemplo', 'apoio': 'Registro profissional não informado'},
            {'titulo': 'PARÂMETROS URBANÍSTICOS', 'cor': 'g', 'destaque': 'Parcela e zoneamento conferidos', 'apoio': 'Zona indicada no projeto: ZXX'},
        ],
        'pontos_confirmar': 'Afastamentos • Permeabilidade • Autoria',
    }


def item(rng, i, sit=None, comid=False):
    sit = sit or ITEM_SIT[i % 4]
    d = {'titulo': f'Item de teste {i:03d}: ' + ' '.join(rng.choice(PALAVRAS) for _ in range(3)), 'situacao': sit,
         'verificacao': texto(rng, 3),
         'providencia': 'Nenhuma correção nessas medidas.' if sit == 'ATENDE' else 'Apresentar ' + texto(rng, 2),
         'base_legal': f'Código de Obras (LC nº 70/1990), art. {100 + i % 60}.',
         'referencia': f'ARQ {i % 9 + 1:02d}/09, planta. Figura {i % 8 + 1}.'}
    if comid: d['id'] = f'it-{i:03d}'
    return d


def fontes():
    return [
        {'rotulo': 'Projeto', 'texto': 'Pranchas ARQ 01/09 a ARQ 09/09, revisão 02, protocolo de 03/02/2026.', 'url': ''},
        {'rotulo': 'Imóvel', 'texto': 'Matrícula fictícia nº 00.000, certidão de 10/01/2026.', 'url': ''},
        {'rotulo': 'Código de Obras', 'texto': 'LC municipal nº 70/1990, arts. 100 a 160.', 'url': 'https://exemplo.invalid/codigo-de-obras'},
    ]


def assinatura():
    return {'nome': 'FULANO DE TAL EXEMPLO', 'cargo': 'ARQUITETO E URBANISTA — CAU/ES 000000-0'}


CONCLUSAO = ('Foram recebidos os documentos desta etapa. Permanecem as pendências dos itens indicados, que precisam ser atendidas para a '
             'continuidade do processo. Os itens que atendem o fazem apenas nos pontos indicados e não autorizam a aprovação do projeto. '
             'Depois da complementação, os itens pendentes serão reanalisados.')


def localizacao(rng, mapa, grupos=None, n_por_grupo=4):
    grupos = grupos or ['Identificação', 'Áreas e edificação', 'Zona e condicionantes', 'Cadastro e entorno']
    dados = []
    for g in grupos:
        for k in range(n_por_grupo):
            x = {'grupo': g, 'rotulo': f'Dado {k + 1}', 'valor': f'{rng.randint(10, 999)},{rng.randint(0, 99):02d} m²'}
            if k == 1: x.update({'chip': 'DIVERGE', 'alerta': 'a'})
            if k == 2: x['nota'] = 'Ressalva curta do campo.'
            dados.append(x)
    return {'subtitulo': 'Conferência do endereço com a base cartográfica e cadastral.', 'situacao': 'A_CONFIRMAR', 'imagem': mapa,
            'dados': dados, 'texto': texto(rng, 2),
            'legenda': [{'tipo': 'lote', 'texto': 'Lote do projeto'}, {'tipo': 'edif', 'texto': 'Edificação no lote'},
                        {'tipo': 'app', 'texto': 'Faixa de APP'}, {'tipo': 'rio', 'texto': "Curso d'água"}],
            'observacoes': [texto(rng, 1), texto(rng, 1)],
            'fonte': 'Base municipal fictícia e cadastro imobiliário fictício; SIRGAS 2000 / UTM 24S.'}


def indices(rng, n):
    return {'subtitulo': 'Parcela declarada: 480,00 m² • parâmetros sujeitos à confirmação.', 'zona': 'ZXX',
            'linhas': [{'icone': ICONES_IND[i % 6], 'nome': f'Índice {i + 1}', 'descricao': frase(rng, 5),
                        'calculo': f'{rng.randint(50, 400)},00 ÷ 480,00 × 100 = {rng.randint(10, 90)}%',
                        'projeto': f'{rng.randint(10, 90)}%', 'projeto_destaque': i % 5 == 3, 'limite': f'≤ {rng.randint(40, 90)}%',
                        'situacao': IND_SIT[i % 4], 'texto': frase(rng, 6)} for i in range(n)],
            'observacao': texto(rng, 2)}


def diagrama(rng):
    anc = ['lote', 'permeavel', 'edificacao', 'lateral', 'telhado', 'altura']
    return {'planta': {'testada': 'Testada (12,00 m)', 'profundidade': 'Profundidade (40,00 m)', 'limites_confirmados': False,
                       'permeavel': {'titulo': 'ÁREA PERMEÁVEL', 'linha1': '18% no projeto', 'linha2': '12% no cálculo', 'linha2_destaque': True},
                       'edificacao': ['EDIFICAÇÃO NOVA', '3 pavimentos'], 'beiral': 'beiral 0,60 m', 'lateral': ['lateral 1,50 m', '(cotada)'],
                       'frente': 'frente 5,00 m', 'fundos': 'fundos não cotados'},
            'volumetria': {'altura_esquerda': ['Corte AA', '9,20 m'], 'altura_direita': ['Corte BB', '9,35 m']},
            'chamadas': [{'ancora': a, 'titulo': f'Chamada {k + 1}', 'situacao': IND_SIT[k % 4], 'texto': frase(rng, 7)} for k, a in enumerate(anc)],
            'nota': 'O esquema não representa a implantação real. Medidas e limites conforme a tabela de índices.'}


def vaos(rng, n):
    return [{'codigo': f'J{k + 1}', 'medidas': f'{rng.randint(60, 200) / 100:.2f} × {rng.randint(60, 150) / 100:.2f} m', 'tipo': 'Janela de correr',
             'area': f'{rng.randint(50, 250) / 100:.2f} m²' if k % 7 else '', 'sem_area': '' if k % 7 else 'VÃO LIVRE NÃO COTADO'} for k in range(n)]


def ambiente(rng, i, n_vaos, resultado=False):
    a = {'icone': ICONES_AMB[i % 5], 'nome': f'Ambiente {i + 1}',
         'detalhes': [f'{rng.randint(1, 8)} ambientes • piso {rng.randint(4, 20)},00 m²', 'Pavimento tipo'],
         'situacao': ['CONTA_CONFERE', 'PENDENTE', 'NAO_ATENDE'][i % 3], 'vaos': vaos(rng, n_vaos),
         'minimo': [{'texto': f'{rng.randint(4, 20)},00 ÷ 5 = {rng.randint(1, 4)},00 m²', 'destaque': True}, {'texto': '(art. 127)', 'destaque': False}],
         'situacao_titulo': '' if i % 2 else 'Conferência por ambiente', 'textos': [frase(rng, 8)]}
    if resultado:
        a['resultado'] = {'total': f'{rng.randint(1, 9)},00 m²', 'conta': 'J1 1,00 + J2 2,00 = 3,00 m²', 'comparacao': 'Mínimo 2,00 m²; confere.'}
    return a


def iluminacao(rng, ambientes):
    return {'subtitulo': 'Conferência de vãos, iluminação e ventilação na edificação.', 'tag': '8 UNIDADES • 3 PAVIMENTOS', 'ambientes': ambientes,
            'abrangencia': [frase(rng, 9), 'Vão de cada esquadria = largura × altura do quadro de esquadrias.'],
            'nao_computadas': ['Nenhuma.'], 'atencao': frase(rng, 9), 'fonte': 'ARQ 02/09 — plantas e quadros; ARQ 05/09 — cortes e fachada.'}


def figura(rng, k, arquivo, notas=4, comid=False):
    f = {'prancha': f'ARQ {k:02d}/09', 'titulo': f'Vista {k}: ' + ' '.join(rng.choice(PALAVRAS) for _ in range(3)), 'imagem': 'img/' + arquivo,
         'texto': texto(rng, 1) if k % 3 == 0 else '',
         'notas': [{'elemento': frase(rng, 5).rstrip('.'), 'texto': '' if j == 2 else 'Apresentar ' + texto(rng, 1), 'itens': f'item {j + k:02d}'}
                   for j in range(notas)],
         'fonte': f'ARQ {k:02d}/09, vista {k}.'}
    if comid: f['id'] = f'fig-{k:02d}'
    return f


def base(rng, itens, **extra):
    d = {'config': {'orgao': 'SECRETARIA MUNICIPAL DE OBRAS'}, 'capa': capa(rng), 'itens': itens}
    d.update(extra)
    d.update({'fontes': fontes(), 'conclusao': CONCLUSAO, 'assinatura': assinatura()})
    return d


# ───────────────────────── casos ─────────────────────────
def caso_curto():
    rng = random.Random(SEMENTE + 1)
    return base(rng, [item(rng, 1, 'ATENDE'), item(rng, 2, 'NAO_COMPROVADO'), item(rng, 3, 'NAO_ATENDE')])


def caso_sem_opcionais():
    rng = random.Random(SEMENTE + 2)
    return {'capa': capa(rng, '9002/2026', 'EMPRESA FICTÍCIA EXEMPLO LTDA'), 'itens': [item(rng, 1, 'DIVERGENCIA_INTERNA')],
            'fontes': [{'rotulo': 'Projeto', 'texto': 'Pranchas do projeto fictício.'}], 'conclusao': 'Aguardando complementação.',
            'assinatura': assinatura()}


def caso_longo():
    rng = random.Random(SEMENTE + 3)
    imagem('mapa.png', 1600, 1000, 'MAPA FICTÍCIO', SEMENTE + 30, mapa=True)
    for k in range(1, 9): imagem(f'fig{k}.png', 1600, 1000, f'FIGURA FICTÍCIA {k}', SEMENTE + 40 + k)
    itens = [item(rng, i, comid=(i % 2 == 0)) for i in range(1, 61)]
    return base(rng, itens, localizacao=localizacao(rng, 'img/mapa.png'), indices=indices(rng, 6), diagrama=diagrama(rng),
                iluminacao=iluminacao(rng, [ambiente(rng, i, 3 + i % 3, resultado=i % 3 == 0) for i in range(12)]),
                figuras=[figura(rng, k, f'fig{k}.png', notas=1 + k % 6, comid=(k % 2 == 1)) for k in range(1, 9)],
                sem_figura=['Item 05 — Assunto fictício: sem figura, porque o desenho não tem ponto a indicar.'])


def caso_extremos():
    rng = random.Random(SEMENTE + 4)
    imagem('mapa_x.png', 1600, 1000, 'MAPA FICTÍCIO', SEMENTE + 50, mapa=True)
    imagem('alta.png', 800, 2400, 'FIGURA ALTA FICTÍCIA', SEMENTE + 51)
    gigante = ''
    while len(gigante) < 9000: gigante += frase(rng, 12) + ' '
    gigante = gigante[:9000]
    especiais = 'Texto com & < > " \' e emoji 😀 🚧 e acentos áéíóúçãõ.\nSegunda linha após quebra.\r\nTerceira, após CRLF. Fim.'
    itens = [item(rng, 1, 'ATENDE'), item(rng, 2, 'NAO_ATENDE'), item(rng, 3, 'NAO_COMPROVADO'), item(rng, 4, 'DIVERGENCIA_INTERNA')]
    itens[1]['verificacao'] = gigante
    itens[2]['verificacao'] = especiais
    itens[2]['titulo'] = 'Título com <b>marcação</b> & "aspas" e \'apóstrofo\' 😀'
    itens[3]['providencia'] = especiais
    itens[0]['base_legal'] = '<script>alert("x")</script> & &amp; &lt;'
    itens[0]['id'] = 'id-com-<especiais>&"'
    ind = indices(rng, 40)
    ind['linhas'][0]['nome'] = 'Nome com & < > " \' 😀'
    ind['linhas'][1]['texto'] = especiais
    loc = localizacao(rng, 'img/mapa_x.png', grupos=['Grupo livre A', 'Grupo livre B', 'Grupo livre A', 'Dados do imóvel'], n_por_grupo=3)
    d = base(rng, itens, localizacao=loc, indices=ind, figuras=[figura(rng, 1, 'alta.png', notas=6)])
    d['figuras'][0]['titulo'] = 'Figura alta & "estreita" <teste> 😀'
    d['capa']['requerente'] = 'REQUERENTE & FILHOS "LTDA" <EXEMPLO> D\'AVILA 😀'
    d['config']['orgao'] = 'SECRETARIA <TESTE> & "CIA" D\'ÓRGÃOS'
    return d


def caso_estouro():
    rng = random.Random(SEMENTE + 5)
    return base(rng, [item(rng, 1, 'ATENDE'), item(rng, 2, 'NAO_COMPROVADO')],
                iluminacao=iluminacao(rng, [ambiente(rng, 0, 80, resultado=True)]))


def gravar(nome: str, d: dict):
    (SAIDA / f'{nome}.json').write_bytes((json.dumps(d, ensure_ascii=False, indent=1) + '\n').encode('utf-8'))


def main():
    SAIDA.mkdir(parents=True, exist_ok=True)
    for nome, f in (('curto', caso_curto), ('sem_opcionais', caso_sem_opcionais), ('longo', caso_longo),
                    ('extremos', caso_extremos), ('estouro', caso_estouro)):
        gravar(nome, f())
        print('gerado', nome)


if __name__ == '__main__':
    main()
