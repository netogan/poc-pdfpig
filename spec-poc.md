## Especificação da POC

# Objetivo

Quero fazer uma POC experimental pra extrair informações de PDF de documentos diversos fiscais/contábeis, como por exemplo: DARF IRPJ, DARF CSLL, DCTFWEB, DCTF MENSAL, NFSE Guarulhos, DCOMP, DECLARAÇÃO DE IMPORTAÇÃO.

# Especificações

- A API deve ser em .NET 8.
- Deve-se utilizar a lib PDFPig para realizar as extrações.
- Deve conter um endpoint para:
  - Receber um payload de quais informações devem ser extraídas, orientações e demais informações estruturadas e de fácil replicação.
    - Adicionei a pasta `/pdf-amostras` onde contem exemplos de todos esses documentos. Alguns as lables se repetem, outros alguns dados estão em tabelas, enfim, existe uma variedade de diferentes aquivos, onde o PDFPig deve conseguir extrair, baseando-se no template/payload que vamos enviar no endpoint.
  - Receber um arquivo para ser extraído
  - O response deve ser as informações extraídas, baseadas na payload e arquivo, retornada em uma estrutura de JSON.
  - Deve ser documentada toda a API, principalmente o endpoint e quais as funcionalidades o PDFPig tem, para uma outra pessoa qualquer, possa instruir uma nova extração de um novo documento que surgir. Deixe clara as capilaridade e funcionalidades existentes na Lib para que qualquer um possa se orientar. Pense que um desenvolvedor Junior possa ser capaz de ler a documentação, preparar o payload e enviar para o endpoint.
  - Crie instruções e as configurações necessárias para poder executar a api com F5 do visual studio para testá-la com os arquivos de amostra.