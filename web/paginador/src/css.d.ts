// folha.css entra no bundle como texto (loader 'text' do esbuild).
declare module '*.css' {
  const conteudo: string;
  export default conteudo;
}
