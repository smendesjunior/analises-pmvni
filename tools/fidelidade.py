"""Verificações de fidelidade do Editor de Relatórios PMVNI contra o gerador do kit.

Uso:  python -I tools/fidelidade.py v1

v1  Paridade do HTML (V1): para cada caso do corpus (tests/corpus), para o dados_modelo.json do modelo oficial
    (com --instrucoes) e, se existir tests/local.json, para os casos locais, roda o gerador do kit
    (gerar_relatorio.py --rascunho --manter-html: nunca grava recibo) e o `Pmvni.Verificador html`, e compara
    os dois HTML byte a byte. Grava tests/saida/v1.json só com {caso, igual, sha256_kit, sha256_editor}.
    Sai com código diferente de zero se algum caso diferir ou falhar.

Regras:
- O gerador do kit é só LIDO (nada é gravado na pasta do kit; sem bytecode: python -I + dont_write_bytecode).
- Tudo o que os dois lados escrevem vai para tests/saida/tmp/<run-id>/ (dentro do repositório, NUNCA em %TEMP% ou
  AppData: o Python iniciado daqui roda dentro do pacote MSIX do app Claude e escreveria em pasta virtualizada,
  que o Edge aberto via explorer.exe não enxerga). A pasta é apagada no fim.
- Casos locais (tests/local.json, ignorado pelo git) são dados reais: o nome nas saídas é só o informado, nada do
  conteúdo é impresso; numa diferença só saem contagens e a posição do primeiro byte diferente.
"""
from __future__ import annotations
import argparse, datetime, hashlib, json, os, shutil, subprocess, sys, time
from pathlib import Path

sys.dont_write_bytecode = True
RAIZ = Path(__file__).resolve().parent.parent
CORPUS = RAIZ / 'tests' / 'corpus'
SAIDA = RAIZ / 'tests' / 'saida'
LOCAL = RAIZ / 'tests' / 'local.json'
MODELO_OFICIAL = RAIZ / 'src' / 'Pmvni.Composicao' / 'ModeloOficial' / 'rev10'
KIT_PADRAO = (Path.home() / 'OneDrive' / 'PMVNI' / 'SIDNEY JUNIOR' / 'KIT DE TREINAMENTO DE IA (PMVNI)'
              / '02 - MODELOS DE DOCUMENTOS' / 'RELATORIO' / 'MODELO HTML (USADO PELOS PLUGINS)')
VERIFICADOR_PROJ = RAIZ / 'src' / 'Pmvni.Verificador' / 'Pmvni.Verificador.csproj'


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


def casos() -> list[dict]:
    """Lista de {nome, dados, instrucoes, local}."""
    lista = [{'nome': p.stem, 'dados': p, 'instrucoes': False, 'local': False} for p in sorted(CORPUS.glob('*.json'))]
    lista.append({'nome': 'dados_modelo', 'dados': MODELO_OFICIAL / 'dados_modelo.json', 'instrucoes': True, 'local': False})
    if LOCAL.is_file():
        for c in json.loads(LOCAL.read_text(encoding='utf-8-sig')).get('casos_locais', []):
            lista.append({'nome': str(c['nome']), 'dados': Path(c['dados']), 'instrucoes': False, 'local': True})
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


def rodar_caso(c: dict, kit: Path, exe: Path, run: Path) -> dict:
    nome = c['nome']
    res = {'caso': nome, 'igual': False, 'sha256_kit': None, 'sha256_editor': None}
    pasta = run / nome
    pasta.mkdir(parents=True, exist_ok=True)
    flags = ['--rascunho', '--manter-html'] + (['--instrucoes'] if c['instrucoes'] else [])
    pdf = pasta / 'kit.pdf'
    r = subprocess.run([sys.executable, '-I', str(kit / 'gerar_relatorio.py'), str(c['dados']), str(pdf), *flags],
                       capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=600)
    html_kit = pdf.with_suffix('.html')
    if r.returncode != 0 or not html_kit.is_file():
        print(f'  [{nome}] kit falhou (código {r.returncode})' + ('' if c['local'] else ': ' + (r.stderr or r.stdout)[-600:]))
        return res
    html_ed = pasta / 'editor.html'
    r2 = subprocess.run([str(exe), 'html', str(c['dados']), str(html_ed), '--rascunho'] + (['--instrucoes'] if c['instrucoes'] else []),
                        capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=300)
    if r2.returncode != 0 or not html_ed.is_file():
        print(f'  [{nome}] Pmvni.Verificador falhou (código {r2.returncode})' + ('' if c['local'] else ': ' + (r2.stderr or r2.stdout)[-600:]))
        return res
    a, b = html_kit.read_bytes(), html_ed.read_bytes()
    res.update(sha256_kit=sha(a), sha256_editor=sha(b), igual=(a == b))
    print(f'  [{nome}] ' + ('IGUAL' if res['igual'] else 'DIFERE: ' + primeira_diferenca(a, b)) + f' ({len(a)} bytes)')
    return res


def v1() -> int:
    kit = pasta_do_kit()
    exe = compilar_verificador()
    run = SAIDA / 'tmp' / ('v1-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
    run.mkdir(parents=True, exist_ok=True)
    resultados = []
    try:
        for c in casos():
            try:
                resultados.append(rodar_caso(c, kit, exe, run))
            except Exception as ex:  # um caso com problema não esconde os outros
                print(f'  [{c["nome"]}] erro: {type(ex).__name__}' + ('' if c['local'] else f': {ex}'))
                resultados.append({'caso': c['nome'], 'igual': False, 'sha256_kit': None, 'sha256_editor': None})
    finally:
        apagar(run)
        try:
            (SAIDA / 'tmp').rmdir()
        except OSError:
            pass
    SAIDA.mkdir(parents=True, exist_ok=True)
    (SAIDA / 'v1.json').write_text(json.dumps(resultados, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    ok = all(r['igual'] for r in resultados)
    print(('V1 OK' if ok else 'V1 FALHOU') + f': {sum(r["igual"] for r in resultados)}/{len(resultados)} casos idênticos')
    return 0 if ok else 1


def main() -> int:
    ap = argparse.ArgumentParser(description='Verificações de fidelidade do Editor de Relatórios PMVNI')
    sub = ap.add_subparsers(dest='cmd', required=True)
    sub.add_parser('v1', help='paridade do HTML com o gerador do kit')
    a = ap.parse_args()
    return {'v1': v1}[a.cmd]()


if __name__ == '__main__':
    sys.exit(main())
