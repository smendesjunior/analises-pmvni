"""Gera as referências de paridade (tests/referencias) rodando as funções do PRÓPRIO kit PMVNI.

Uso:  python -I tools/gerar_referencias.py [--kit <pasta do modelo HTML do kit>]

A pasta do kit vem de --kit, da variável PMVNI_KIT_DIR ou do caminho padrão deste PC. O kit é só LIDO:
o módulo é importado por caminho, sem gravar bytecode, e nada é escrito lá. As saídas vão para
tests/referencias (versionadas): os testes em C# comparam byte a byte com elas, sem precisar do kit.
Todos os dados de entrada abaixo são fictícios.
"""
from __future__ import annotations
import importlib.util, json, os, shutil, sys, tempfile, textwrap
from pathlib import Path

sys.dont_write_bytecode = True
KIT_PADRAO = (Path.home() / 'OneDrive' / 'PMVNI' / 'SIDNEY JUNIOR' / 'KIT DE TREINAMENTO DE IA (PMVNI)'
              / '02 - MODELOS DE DOCUMENTOS' / 'RELATORIO' / 'MODELO HTML (USADO PELOS PLUGINS)')
RAIZ = Path(__file__).resolve().parent.parent
SAIDA = RAIZ / 'tests' / 'referencias'


def carregar_kit(pasta: Path):
    arq = pasta / 'gerar_relatorio.py'
    if not arq.is_file():
        raise SystemExit(f'gerar_relatorio.py não encontrado em {pasta} (use --kit ou PMVNI_KIT_DIR)')
    spec = importlib.util.spec_from_file_location('gerar_relatorio_kit', arq)
    mod = importlib.util.module_from_spec(spec)
    sys.modules['gerar_relatorio_kit'] = mod
    spec.loader.exec_module(mod)
    return mod, arq


def bloco_localizacao(arq: Path, mod):
    """O agrupamento da localização vive dentro de main(); extrai as linhas do próprio kit e as executa
    como função, para a referência vir do código do kit e não de uma cópia nossa."""
    linhas = arq.read_text(encoding='utf-8').splitlines()
    ini = next(i for i, l in enumerate(linhas) if l.strip() == 'grupos = []')
    fim = next(i for i, l in enumerate(linhas) if l.strip().startswith("leg = d['localizacao']"))
    corpo = textwrap.dedent('\n'.join(linhas[ini:fim + 1]))
    fonte = 'def _agrupar(d):\n' + textwrap.indent(corpo + '\nreturn grupos, colunas, leg', '    ')
    ns = dict(vars(mod))
    exec(compile(fonte, str(arq) + '#localizacao', 'exec'), ns)
    return ns['_agrupar']


def gravar_texto(nome: str, s: str):
    (SAIDA / nome).write_bytes(s.encode('utf-8'))


def gravar_json(nome: str, obj):
    gravar_texto(nome, json.dumps(obj, ensure_ascii=False, indent=1) + '\n')


def tentar(f, *a):
    try:
        return f(*a)
    except Exception as ex:  # a referência guarda a mensagem do erro do kit
        return 'ERRO: ' + str(ex)


# ───────────────────────── dados fictícios ─────────────────────────
def planta(conf, destaque, dois, via=True, beiral=True, linha2=True):
    p = {'limites_confirmados': conf, 'testada': '10,00 m & "frente"', 'profundidade': "30,50 m",
         'permeavel': {'titulo': 'ÁREA PERMEÁVEL', 'linha1': '25,0% do lote'},
         'edificacao': ['EDIFICAÇÃO', '120,40 m²'] if dois else ['EDIFICAÇÃO'],
         'lateral': ['1,50 m', 'afast. <lateral>'] if dois else ['1,50 m'],
         'frente': '5,00 m', 'fundos': '3,00 m'}
    if linha2: p['permeavel']['linha2'] = 'mínimo 20,0%'
    if destaque: p['permeavel']['linha2_destaque'] = True
    if via: p['via'] = "RUA DAS FLORES 'TESTE'"
    if beiral: p['beiral'] = 'beiral 0,50 m'
    return p


def volume(dois, via=True):
    v = {'altura_esquerda': ['7,20 m', 'cumeeira'] if dois else ['7,20 m'],
         'altura_direita': ['6,00 m', 'platibanda & muro'] if dois else ['6,00 m']}
    if via: v['via'] = 'RUA DOS TESTES'
    return v


PLANTAS = {
    'confirmado_todas_destaque_duas': (planta(True, True, True), [['lote', 1], ['permeavel', 2], ['edificacao', 3], ['lateral', 4]]),
    'nao_confirmado_sem_marcas_uma': (planta(False, False, False, via=False, beiral=False, linha2=False), []),
    'confirmado_sem_marcas_destaque_duas': (planta(True, True, True, via=False), []),
    'nao_confirmado_duas_marcas_uma': (planta(False, False, False), [['lateral', 1], ['lote', 2]]),
}
VOLUMES = {
    'duas_marcas_duas': (volume(True), [['telhado', 1], ['altura', 2]]),
    'sem_marcas_uma': (volume(False, via=False), []),
    'uma_marca_uma': (volume(False), [['altura', 1]]),
    'sem_marcas_duas': (volume(True), []),
}


def itens(*sits):
    return [{'situacao': s, 'titulo': f'Item {i}'} for i, s in enumerate(sits, 1)]


RESUMOS = {
    'so_atende': itens('ATENDE', 'ATENDE'),
    'todas': itens('ATENDE', 'NAO_ATENDE', 'NAO_COMPROVADO', 'DIVERGENCIA_INTERNA', 'NAO_COMPROVADO', 'ATENDE', 'NAO_ATENDE'),
    'sem_atende_nem_divergencia': itens('NAO_COMPROVADO', 'NAO_COMPROVADO', 'NAO_COMPROVADO'),
    'vazio': [],
}


def item_ok(**mais):
    it = {'situacao': 'ATENDE', 'titulo': 'Recuo frontal', 'verificacao': 'Conferido na prancha 01.',
          'providencia': 'Nenhuma.', 'base_legal': 'Art. 1º da Lei Fictícia nº 0/2000', 'referencia': 'Prancha 01'}
    it.update(mais)
    return it


def dados_ok():
    return {
        'capa': {'protocolo': '01/02/2099', 'rotulo_data': 'ANÁLISE', 'data': '03/02/2099',
                 'processo': '001/2099', 'requerente': 'Requerente fictício', 'pontos_confirmar': 'Área permeável.',
                 'quadros': [{'titulo': t, 'destaque': d, 'apoio': a} for t, d, a in (
                     ('Localização', 'Rua Exemplo', 'Lote 2'), ('Caracterização', 'Residencial', 'Uma unidade'),
                     ('Autoria indicada', 'Autor fictício', 'Prancha 01'), ('Parâmetros', 'Zona de teste', 'Mapa fictício'))]},
        'itens': [item_ok(), item_ok(situacao='NAO_COMPROVADO', titulo='Área permeável')],
        'fontes': [{'rotulo': 'Norma fictícia', 'texto': 'Art. 1º da Lei Fictícia nº 0/2000'}],
        'conclusao': 'Conclusão fictícia.',
        'assinatura': {'nome': 'Fulano de Tal', 'cargo': 'Analista fictício'},
        'indices': {'linhas': [
            dict(nome='TO', situacao='CONTA_CONFERE', descricao='Taxa de ocupação', calculo='40/100', projeto='40%', limite='50%', texto='Dentro do limite.'),
            dict(nome='CA', situacao='PENDENTE', descricao='Coeficiente', calculo='120/100', projeto='1,2', limite='A confirmar na norma.', texto='Verificar enquadramento.')]},
        'diagrama': {'chamadas': [{'titulo': 'Projeção', 'texto': 'Comparar com a prancha 01.'}]},
        'iluminacao': {'subtitulo': 'Vãos da sala', 'tag': 'Projeto', 'fonte': 'Prancha 01', 'ambientes': [{'nome': 'Sala', 'situacao': 'DIVERGENCIA'}]},
        'figuras': [{'imagem': 'fig1.png', 'prancha': '01', 'titulo': 'Planta', 'fonte': 'Projeto fictício', 'notas': []}],
        'localizacao': {'situacao': 'CONFIRMADO', 'imagem': 'mapa.png', 'subtitulo': 'Consulta cadastral', 'texto': 'Lote fictício identificado.', 'fonte': 'Base de teste',
                        'dados': [{'rotulo': 'Inscrição', 'valor': '1'}, {'rotulo': 'Lote', 'valor': '2'}, {'rotulo': 'Rua', 'valor': '3'}]},
    }


def casos_conferir():
    ok = dados_ok()
    imgs_ok = ['fig1.png', 'mapa.png']
    c = {}
    c['valido'] = (ok, imgs_ok, False)
    d = dados_ok()
    del d['capa']['protocolo']
    d['fontes'][0]['texto'] = ' '
    d['assinatura']['cargo'] = None
    c['campos_sem_conteudo'] = (d, imgs_ok, False)
    d = dados_ok(); d['itens'][0]['situacao'] = 'ATENDEU'; d['itens'][1]['situacao'] = 12.0
    d['itens'].append(item_ok(situacao=3)); d['itens'].append(item_ok(situacao=None, verificacao='  ', base_legal=None))
    d['indices']['linhas'][0]['situacao'] = 'NAO_COMPROVADO'; d['iluminacao']['ambientes'][0]['situacao'] = 'CONFIRMADO'
    c['situacao_errada'] = (d, imgs_ok, False)
    d = dados_ok(); d['figuras'].append({'imagem': 'sumiu.png'}); d['figuras'].append({'notas': []})
    d['localizacao']['imagem'] = 'mapa_ausente.png'
    c['figura_sem_imagem'] = (d, imgs_ok, False)
    d = dados_ok(); d['figuras'][0]['notas'] = ['1', '2', '3', '4', '5', '6', '7']
    c['sete_notas'] = (d, imgs_ok, False)
    d = dados_ok()
    d['conclusao'] = 'Falta [DD/MM/AAAA] e [Nº]/[ANO]; também [x] e [ab]. [ÁREA] [zeta] [Zeta] [😀 astral] [￮ fim]'
    d['capa']['quadros'][0]['destaque'] = '[Endereço do carimbo, nº, bairro]'
    d['itens'][0]['verificacao'] = '[aa] [bb] [cc] [a] [b] [c] [d] [e] [f] [g] [h] [i] [j] [k]'
    d['assinatura']['nome'] = '[' + 'x' * 80 + '] [' + 'y' * 81 + '] [multi\nlinha] [[aninhado]]'
    c['colchetes'] = (d, imgs_ok, False)
    c['colchetes_rascunho'] = (d, imgs_ok, True)
    c['ausentes'] = ({}, [], False)
    d = dados_ok(); d['capa']['quadros'] = d['capa']['quadros'][:3]
    d['localizacao'] = {'situacao': 'A_CONFIRMAR', 'imagem': 'mapa.png', 'dados': [{'rotulo': 'a'}], 'texto': '   '}
    c['localizacao_e_capa'] = (d, imgs_ok, False)
    d = dados_ok(); d['localizacao'] = {'situacao': 'TALVEZ', 'dados': []}
    c['localizacao_invalida'] = (d, imgs_ok, False)
    d = dados_ok(); d['indices'] = None; d['iluminacao'] = {}
    c['indices_nulos'] = (d, imgs_ok, False)
    return c


def loc(dados, **mais):
    r = {'situacao': 'CONFIRMADO', 'imagem': 'mapa.png', 'dados': dados}
    r.update(mais)
    return r


def x(rot, grupo=None):
    r = {'rotulo': rot, 'valor': rot.lower()}
    if grupo is not None: r['grupo'] = grupo
    return r


LOCALIZACOES = {
    'ordem_conhecida': loc([x('a', 'Zona e condicionantes'), x('b', 'Identificação'), x('c', 'Áreas e edificação'),
                            x('d', 'Cadastro e entorno'), x('e', 'Zona e condicionantes'), x('f', 'Identificação')]),
    'titulos_livres': loc([x('a', 'Grupo A'), x('b', 'Grupo A'), x('c', 'Grupo A'), x('d', 'Grupo B'), x('e', 'Grupo C'),
                           x('f', 'Grupo C'), x('g', 'Grupo D')]),
    'sem_grupo': loc([x('a'), x('b', ''), x('c', None)]),
    'mistura_conhecido_e_livre': loc([x('a', 'Identificação'), x('b', 'Outro'), x('c', 'Áreas e edificação')]),
    'so_uma_coluna': loc([x('a', 'Identificação'), x('b', 'Cadastro e entorno')],
                         legenda=[{'tipo': 'lote', 'texto': 'Só o lote'}]),
    'legenda_vazia_e_sem_dados': loc([], legenda=[]),
    'legenda_nula': loc([x('a', 'Identificação')], legenda=None),
}


def main():
    a = sys.argv[1:]
    kit = Path(os.environ.get('PMVNI_KIT_DIR') or KIT_PADRAO)
    if '--kit' in a:
        kit = Path(a[a.index('--kit') + 1])
    mod, arq = carregar_kit(kit)
    agrupar = bloco_localizacao(arq, mod)
    SAIDA.mkdir(parents=True, exist_ok=True)
    for velho in SAIDA.iterdir():  # só os arquivos: a pasta pode estar presa pelo OneDrive/Git
        if velho.is_file(): velho.unlink()

    for k in mod.SIT:
        gravar_texto(f'pill_{k}.txt', mod.pill(k))
        gravar_texto(f'cor_{k}.txt', mod.FILTROS['cor'](k))
    gravar_texto('pill_erro.txt', tentar(mod.pill, 'XX'))
    gravar_texto('cor_erro.txt', tentar(mod.FILTROS['cor'], 'XX'))
    gravar_texto('cor_erro_nulo.txt', tentar(mod.FILTROS['cor'], None))
    for k in mod.IC:
        gravar_texto(f'icone_{k}.txt', mod.icone(k))
    gravar_texto('icone_erro.txt', tentar(mod.icone, 'zz'))
    gravar_json('tabelas.json', {'SIT': {k: list(v) for k, v in mod.SIT.items()}, 'SIT_LOCAL': sorted(mod.SIT_LOCAL),
                                 'SIT_ITEM': sorted(mod.SIT_ITEM), 'SIT_INDICE': sorted(mod.SIT_INDICE),
                                 'LEGENDA_PADRAO': mod.LEGENDA_PADRAO,
                                 'POS_PLANTA': {k: list(v) for k, v in mod.POS_PLANTA.items()},
                                 'POS_VOLUME': {k: list(v) for k, v in mod.POS_VOLUME.items()}})

    gravar_texto('cabecalho_padrao.txt', str(mod.cabecalho({})))
    gravar_texto('cabecalho_orgao.txt', str(mod.cabecalho({'orgao': 'SECRETARIA <TESTE> & "CIA" D\'ÓRGÃOS'})))

    for nome, (p, marcas) in PLANTAS.items():
        gravar_json(f'planta_{nome}.entrada.json', {'p': p, 'marcas': marcas})
        gravar_texto(f'planta_{nome}.txt', mod.planta_svg(p, [tuple(m) for m in marcas]))
    for nome, (v, marcas) in VOLUMES.items():
        gravar_json(f'volumetria_{nome}.entrada.json', {'v': v, 'marcas': marcas})
        gravar_texto(f'volumetria_{nome}.txt', mod.volumetria_svg(v, [tuple(m) for m in marcas]))

    diag = {'planta': PLANTAS['confirmado_todas_destaque_duas'][0], 'volumetria': VOLUMES['duas_marcas_duas'][0],
            'titulo': 'Diagrama fictício',
            'chamadas': [{'ancora': 'lote', 'situacao': 'CONTA_CONFERE', 'texto': 'um'},
                         {'ancora': 'telhado', 'situacao': 'PENDENTE', 'texto': 'dois'},
                         {'ancora': 'permeavel', 'situacao': 'NAO_ATENDE', 'texto': 'três'},
                         {'ancora': 'altura', 'situacao': 'DIVERGENCIA', 'texto': 'quatro'},
                         {'ancora': 'edificacao', 'situacao': 'CONTA_CONFERE'}, {'ancora': 'lateral', 'situacao': 'PENDENTE'}]}
    gravar_json('diagrama_ok.entrada.json', diag)
    gravar_json('diagrama_ok.json', mod.montar_diagrama(diag))
    ruim = dict(diag, chamadas=[{'ancora': 'lote', 'situacao': 'CONTA_CONFERE'}, {'ancora': 'sotao', 'situacao': 'PENDENTE'}])
    gravar_json('diagrama_erro_ancora.entrada.json', ruim)
    gravar_texto('diagrama_erro_ancora.txt', tentar(mod.montar_diagrama, ruim))
    ruim = dict(diag, chamadas=[{'ancora': 'lote', 'situacao': 'CONTA_CONFERE'}, {'ancora': 'altura', 'situacao': 'ATENDE'}])
    gravar_json('diagrama_erro_situacao.entrada.json', ruim)
    gravar_texto('diagrama_erro_situacao.txt', tentar(mod.montar_diagrama, ruim))

    for nome, its in RESUMOS.items():
        gravar_json(f'resumo_{nome}.entrada.json', its)
        gravar_texto(f'resumo_{nome}.txt', mod.resumo(its))

    for nome, (d, imgs, rascunho) in casos_conferir().items():
        tmp = Path(tempfile.mkdtemp(prefix='ref_conferir_'))
        try:
            for im in imgs: (tmp / im).write_bytes(b'x')
            erros = mod.conferir(d, tmp, rascunho)
        finally:
            shutil.rmtree(tmp, ignore_errors=True)
        gravar_json(f'conferir_{nome}.json', {'dados': d, 'imagens': imgs, 'rascunho': rascunho, 'erros': erros})

    for nome, l in LOCALIZACOES.items():
        grupos, colunas, leg = agrupar({'localizacao': l})
        colunas = [c for c in colunas if c]  # como na montagem do contexto no kit: ctx['localizacao'] = dict(..., colunas=[c for c in colunas if c], ...)
        gravar_json(f'localizacao_colunas_{nome}.json', {'entrada': l, 'grupos': grupos, 'colunas': colunas, 'legenda': leg})

    print('REFERENCIAS_GERADAS', len(list(SAIDA.iterdir())), 'arquivos em', SAIDA)


if __name__ == '__main__':
    main()
