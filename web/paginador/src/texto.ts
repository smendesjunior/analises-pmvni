// Texto por folha: o texto visível da área útil de cada section.folha, na ordem, com os espaços normalizados.
// É a medida de "mesmo conteúdo em cada folha" entre a composição (host oculto), a tela e o PDF.

/** Espaços (inclusive quebras de linha) colapsados em um só, sem espaço nas pontas. */
export const normalizar = (s: string): string => s.replace(/\s+/g, ' ').trim();

/**
 * Texto de cada folha (só a área útil; cabeçalho e rodapé ficam fora). innerText ignora o que não é renderizado
 * (marcadores ocultos com data-pmvni-oculto, display:none) e não depende de onde o texto quebra em linhas depois de
 * normalizado. Sem folhas, lista vazia.
 */
export function textoPorFolha(): string[] {
  return Array.from(document.querySelectorAll<HTMLElement>('section.folha')).map((f) => {
    const area = f.querySelector<HTMLElement>(':scope > .area');
    return area ? normalizar(area.innerText) : '';
  });
}
