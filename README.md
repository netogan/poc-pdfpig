# POC PdfPig — Extração de dados de documentos fiscais/contábeis

API em **.NET 8** que extrai informações de PDFs (DARF, DCTF, DCTFWeb, DCOMP/PER, NFS-e, DI, guia do FGTS…) usando a biblioteca **[PdfPig](https://github.com/UglyToad/PdfPig)**.

A ideia central: **você não escreve código para cada documento novo**. Você escreve um **template JSON** dizendo *o que* extrair e *como achar* cada informação (pelo rótulo, por regex, por tabela, por região da página…). A API aplica o template ao PDF e devolve um JSON com os dados.

```
PDF  +  template.json  ──►  POST /api/extraction  ──►  { "data": { "cnpj": "...", "debitos": [ ... ] } }
```

---

## Sumário

1. [Estrutura do repositório](#estrutura-do-repositório)
2. [Como executar (F5 no Visual Studio)](#como-executar-f5-no-visual-studio)
3. [Testando com os PDFs de amostra](#testando-com-os-pdfs-de-amostra)
4. [Endpoints](#endpoints)
5. [Templates prontos](#templates-prontos)
6. [Documentação detalhada](#documentação-detalhada)

---

## Estrutura do repositório

```
poc-pdfpig/
├── PocPdfPig.sln                     ← abra este arquivo no Visual Studio
├── pdf-amostras/                     ← PDFs de exemplo (NÃO versionados — ver aviso abaixo)
├── docs/
│   ├── 01-guia-da-api.md             ← endpoints, requests e responses
│   ├── 02-referencia-template.md     ← TODAS as opções do template (o "payload")
│   ├── 03-pdfpig-recursos.md         ← o que a lib PdfPig oferece e como a POC usa
│   └── 04-passo-a-passo-novo-documento.md ← tutorial: criando o template de um documento novo
├── src/PocPdfPig.Api/
│   ├── Controllers/                  ← endpoints HTTP
│   ├── Extraction/                   ← motor de extração (layout, estratégias, conversões)
│   ├── Models/                       ← template, resultado, requests
│   ├── Services/                     ← parser/validador de template, armazenamento, serviço
│   ├── Templates/                    ← templates prontos (*.json) para cada documento das amostras
│   └── PocPdfPig.Api.http            ← requisições prontas para o Visual Studio / VS Code
└── tests/PocPdfPig.Tests/            ← testes que rodam todos os templates contra as amostras
```

> ⚠️ **Os PDFs de amostra não estão no repositório.** Eles são documentos fiscais reais (com CPFs, CNPJs e dados bancários) e por isso a pasta `pdf-amostras/` está no `.gitignore`.
> Para usar os endpoints `/api/samples` e rodar os testes de extração, crie a pasta `pdf-amostras/` na raiz do repositório e copie para ela os PDFs (peça-os ao time). Os nomes esperados pelos testes são: `darf-irpj.pdf`, `darf-csll.pdf`, `darf-pagamentos.pdf`, `dctfweb.pdf`, `dctf.pdf`, `dcomp1.pdf`, `dcomp2.pdf`, `perdcom.pdf`, `nota_fiscal_servico_guarulhos_sp.pdf`, `declaracao_importacao.pdf` e `guia_recisoria_fgts.pdf`.
> Sem a pasta, a API funciona normalmente com upload (`POST /api/extraction`) e os testes que dependem das amostras são **pulados** (não falham).

---

## Como executar (F5 no Visual Studio)

### Pré-requisitos

- **Visual Studio 2022** (17.8 ou superior) com a carga de trabalho *ASP.NET e desenvolvimento Web*;
- **SDK do .NET 8** (`dotnet --list-sdks` deve listar um `8.0.x`).

### Passo a passo

1. Abra `PocPdfPig.sln` no Visual Studio.
2. No *Gerenciador de Soluções*, clique com o botão direito em **PocPdfPig.Api** → **Definir como Projeto de Inicialização** (normalmente já vem selecionado).
3. Na barra de ferramentas, escolha o perfil **https** (ou **http**) ao lado do botão ▶.
4. Pressione **F5**.
5. O navegador abre o **Swagger** em `https://localhost:7011/swagger` (perfil *http*: `http://localhost:5191/swagger`).

> Na primeira execução com *https* o Visual Studio pode pedir para confiar no certificado de desenvolvimento. Aceite. Se preferir evitar, use o perfil **http**.
> Pela linha de comando: `dotnet dev-certs https --trust`.

### Sem Visual Studio (linha de comando / VS Code)

```bash
cd src/PocPdfPig.Api
dotnet run --launch-profile http
# abra http://localhost:5191/swagger
```

### Rodando os testes

```bash
dotnet test
```

Os testes executam **todos os templates prontos** contra os PDFs de `pdf-amostras/` e conferem valores-chave. Rode sempre que alterar o motor ou um template. Se a pasta `pdf-amostras/` não existir, esses testes aparecem como **ignorados** (skipped).

### Configuração

Em `src/PocPdfPig.Api/appsettings.json`:

```json
"Poc": {
  "TemplatesFolder": "Templates",          // pasta dos templates prontos (relativa ao projeto)
  "SamplesFolder": "../../pdf-amostras"    // pasta dos PDFs de amostra (relativa ao projeto)
}
```

---

## Testando com os PDFs de amostra

Com a API rodando (F5), você tem três caminhos — do mais simples ao mais "real":

### 1. Swagger, sem upload (mais rápido)

Os endpoints **`/api/samples`** leem os PDFs direto da pasta `pdf-amostras`:

| O que fazer | Endpoint |
|---|---|
| Listar amostras | `GET /api/samples` |
| Ver como o PDF é lido ("raio-X") | `GET /api/samples/{fileName}/inspect` |
| Extrair com um template pronto | `POST /api/samples/{fileName}/extract/{templateName}` |
| Extrair com um template que você está escrevendo | `POST /api/samples/{fileName}/extract` (template no corpo) |

Exemplo: em `POST /api/samples/{fileName}/extract/{templateName}` clique em **Try it out**, informe `fileName = darf-irpj.pdf`, `templateName = darf-comprovante-arrecadacao` e **Execute**.

### 2. Swagger, com upload (o fluxo "de produção")

Em `POST /api/extraction` clique em **Try it out**:

- `file`: escolha um PDF de `pdf-amostras/`;
- `template`: cole o JSON de um template (copie de `GET /api/templates/{name}` ou de `src/PocPdfPig.Api/Templates/`);
- `debug`: `true` para ver onde cada valor foi encontrado.

Ou use `POST /api/extraction/{templateName}` enviando só o arquivo.

### 3. Arquivo `.http` (Visual Studio / VS Code)

Abra `src/PocPdfPig.Api/PocPdfPig.Api.http` e clique em **Send request** acima de cada requisição.

### 4. curl / PowerShell

```bash
# upload + template salvo
curl -X POST http://localhost:5191/api/extraction/dctfweb -F "file=@pdf-amostras/dctfweb.pdf"

# upload + template enviado no request (o "<" faz o curl ler o conteúdo do arquivo)
curl -X POST http://localhost:5191/api/extraction \
  -F "file=@pdf-amostras/darf-irpj.pdf" \
  -F "template=<src/PocPdfPig.Api/Templates/darf-comprovante-arrecadacao.json" \
  -F "debug=true"
```

```powershell
# PowerShell 7+
Invoke-RestMethod -Method Post -Uri http://localhost:5191/api/extraction/dctfweb `
  -Form @{ file = Get-Item .\pdf-amostras\dctfweb.pdf } | ConvertTo-Json -Depth 20
```

---

## Endpoints

| Método | Rota | Para quê |
|---|---|---|
| `POST` | `/api/extraction` | **Principal.** Recebe `file` (PDF) + `template` (JSON) e devolve os dados extraídos |
| `POST` | `/api/extraction/{templateName}` | Igual, usando um template salvo em `Templates/` |
| `POST` | `/api/inspect` | "Raio-X" do PDF: linhas, células, palavras e coordenadas |
| `GET` | `/api/templates` | Lista os templates prontos |
| `GET` | `/api/templates/{name}` | Devolve o JSON de um template pronto |
| `POST` | `/api/templates/validate` | Valida um template (sem PDF) |
| `GET` | `/api/samples` | Lista os PDFs de amostra |
| `GET` | `/api/samples/{fileName}/inspect` | Raio-X de uma amostra |
| `POST` | `/api/samples/{fileName}/extract/{templateName}` | Extrai uma amostra com template pronto |
| `POST` | `/api/samples/{fileName}/extract` | Extrai uma amostra com o template enviado no corpo |

Detalhes, exemplos e códigos de erro: [docs/01-guia-da-api.md](docs/01-guia-da-api.md).

---

## Templates prontos

| Template | Documento | Amostra(s) | Destaques |
|---|---|---|---|
| `darf-comprovante-arrecadacao` | Comprovante de Arrecadação DARF (IRPJ, CSLL…) | `darf-irpj.pdf`, `darf-csll.pdf` | `group` por página, `labelBelow`, `table`, regex com vários grupos |
| `darf-relacao-pagamentos` | Relação de pagamentos DARF | `darf-pagamentos.pdf` | `table` de 1000 linhas atravessando 44 páginas |
| `dctfweb` | DCTFWeb (declaração completa) | `dctfweb.pdf` | `labelRight`, `group` repetido de débitos |
| `dctf-mensal` | DCTF Mensal 3.6 | `dctf.pdf` | `group` dentro de `group`, `ignoreLines`, `multiline` |
| `dcomp` | PER/DCOMP – Declaração de Compensação | `dcomp1.pdf`, `dcomp2.pdf` | blocos que atravessam páginas, `occurrence: -1`, `(?-i)` |
| `perdcomp-ressarcimento` | PER/DCOMP – Pedido de Ressarcimento | `perdcom.pdf` | tabela com colunas dinâmicas (meses) |
| `nfse-guarulhos` | NFS-e Guarulhos | `nota_fiscal_servico_guarulhos_sp.pdf` | quadros prestador/tomador com rótulos repetidos |
| `declaracao-importacao` | Extrato da DI | `declaracao_importacao.pdf` | colunas fixas (`left`/`right`), `all: true` |
| `guia-fgts-digital` | GFD – Guia do FGTS Digital | `guia_recisoria_fgts.pdf` | `region` relativa ao rótulo |

---

## Documentação detalhada

| Documento | Conteúdo |
|---|---|
| [01 – Guia da API](docs/01-guia-da-api.md) | Cada endpoint com exemplos de request/response e erros |
| [02 – Referência do template](docs/02-referencia-template.md) | **Todas** as propriedades do template, estratégias e conversões |
| [03 – Recursos do PdfPig](docs/03-pdfpig-recursos.md) | O que a biblioteca oferece, o que a POC usa, limitações e como estender |
| [04 – Passo a passo: documento novo](docs/04-passo-a-passo-novo-documento.md) | Tutorial completo + checklist + solução de problemas |

> **Começando agora?** Leia o [passo a passo](docs/04-passo-a-passo-novo-documento.md) e use a [referência do template](docs/02-referencia-template.md) como consulta.
