# Motor PDF · auditoria da frente 3.1

O Windows PowerShell 5.1 transformava stderr informativo do uv em NativeCommandError durante a captura com a preferência Stop. As chamadas nativas agora preservam stdout, verificam o código de saída e incluem até oito registros recentes de stderr nas falhas. O cartão e a primeira abertura comparam as datas do resumo e do log. Em empate, prevalece o resumo.

O Docling já era carregado na ferramenta de conversão. Blueprint e FYP já executavam em processo próprio com limite de 120 segundos. O PyMuPDF passou a ser importado dentro das ferramentas. A entrada do processo de visão fica fechada. Em teste no runtime instalado, o servidor respondeu ao initialize e encerrou com código zero após EOF, junto com seu único filho. O teste cobriu uma sessão ociosa após o initialize.

A limpeza roda depois da ativação da janela e ao fim da configuração dos motores. Preserva os hashes dos locks, os ambientes apontados, o bootstrap em execução e o GUID mais recente. Consulta processos, respeita as travas de instalação e ignora árvores com links. A retenção de removidos é de 14 dias. A limpeza das sobras do atualizador depende da abertura da cópia de uso. MotorPdf/limpeza.log registra caminho e tamanho dos arquivos apagados. A alocação física pode diferir em arquivos compactados ou com hard links.

Pelo inventário informado, env-6A10E8F8297B12AD10F1 representa cerca de **327 MB candidatos**. Em 21/11, as pastas de 13/11 continuam dentro da retenção. Os 888 MB da visão e o ambiente PDF corrente ficam protegidos.

Os testes em tests/motor-pdf.ps1 passaram no Windows PowerShell, inclusive limpeza numa árvore fictícia. O teste de precedência dos registros foi incluído em PrimeiraAberturaTestes. O build e a suíte .NET ficaram bloqueados pela restauração de pacotes, com NU1301 no acesso ao NuGet.
