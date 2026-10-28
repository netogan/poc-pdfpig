# 01 – Guia da API

URL base local: `http://localhost:5191` (perfil *http*) ou `https://localhost:7011` (perfil *https*).
Swagger: `/swagger` — todos os endpoints podem ser testados por lá ("Try it out").

- [Extração (principal)](#extração)
- [Inspeção ("raio-X" do PDF)](#inspeção)
- [Templates](#templates)
- [Amostras (atalhos para teste)](#amostras)
- [Formato da resposta de extração](#formato-da-resposta-de-extração)
- [Erros](#erros)

---

## Extração

### `POST /api/extraction`

O endpoint principal. Recebe o PDF **e** o template no mesmo request.

**Content-Type:** `multipart/form-data`

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `file` | arquivo | sim | O PDF |
| `template` | texto (JSON) | sim | O template. Veja a [referência](02-referencia-template.md) |
| `debug` | boolean | não | `true` inclui o `trace` (onde cada valor foi achado) |

```bash
curl -X POST http://localhost:5191/api/extraction \
  -F "file=@pdf-amostras/dctfweb.pdf" \
  -F 'template={"fields":[{"name":"cnpj","type":"labelRight","label":"CNPJ"}]}'
```

Resposta:

```json
{
  "template": null,
  "fileName": "dctfweb.pdf",
  "pageCount": 1,
  "success": true,
  "elapsedMs": 9,
  "data": { "cnpj": "32.608.999/0001-42" },
  "warnings": []
}
```

**Chamando de C#:**

```csharp
using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5191") };
using var form = new MultipartFormDataContent
{
    { new ByteArrayContent(File.ReadAllBytes("dctfweb.pdf")), "file", "dctfweb.pdf" },
    { new StringContent(File.ReadAllText("Templates/dctfweb.json")), "template" }
};
var response = await http.PostAsync("/api/extraction", form);
var json = await response.Content.ReadAsStringAsync();
```

### `POST /api/extraction/{templateName}`

Mesma coisa, mas usa um template salvo em `src/PocPdfPig.Api/Templates/{templateName}.json`.

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `file` | arquivo | sim | O PDF |
| `debug` | boolean | não | Inclui o `trace` |

```bash
curl -X POST http://localhost:5191/api/extraction/dctf-mensal -F "file=@pdf-amostras/dctf.pdf"
```

---

## Inspeção

### `POST /api/inspect`

Mostra **como o motor enxerga o PDF**. É o primeiro passo para escrever um template: você descobre os textos dos rótulos, em que linha está cada valor, se o valor está à direita ou abaixo do rótulo, as coordenadas etc.

**Content-Type:** `multipart/form-data`

| Campo | Padrão | Descrição |
|---|---|---|
| `file` | — | O PDF |
| `pages` | todas | Ex.: `1`, `1-3`, `2,5`, `3-last` |
| `includeWords` | `false` | Inclui cada palavra com coordenadas |
| `includeBlocks` | `false` | Inclui os blocos de texto detectados pelo algoritmo *Docstrum* do PdfPig |
| `includeContentOrderText` | `false` | Inclui o texto na ordem em que foi gravado no arquivo |
| `lineTolerance` | `3` | Mesma configuração do template (ver referência) |
| `cellGap` | `8` | Mesma configuração do template (ver referência) |

Resposta (resumida):

```json
{
  "fileName": "darf-irpj.pdf",
  "pageCount": 4,
  "metadata": { "Producer": "...", "CreationDate": "...", "PageCount": "4", "Version": "1.4" },
  "formFields": {},
  "pages": [
    {
      "number": 1, "width": 595, "height": 842, "letterCount": 913, "imageCount": 1,
      "text": "Comprovante de Arrecadação\n...\nCNPJ Razão Social\n34.335.332/0001-49 K2I INTERMEDIACAO LTDA.\n...",
      "lines": [
        {
          "index": 2, "y": 659.8, "left": 22, "right": 262.3,
          "text": "CNPJ Razão Social",
          "cells": [
            { "text": "CNPJ", "left": 22, "bottom": 659.8, "right": 37.7, "top": 664 },
            { "text": "Razão Social", "left": 227, "bottom": 659.8, "right": 262.3, "top": 664.2 }
          ]
        },
        {
          "index": 3, "y": 644.8, "text": "34.335.332/0001-49 K2I INTERMEDIACAO LTDA.",
          "cells": [
            { "text": "34.335.332/0001-49", "left": 101.7, "right": 218 },
            { "text": "K2I INTERMEDIACAO LTDA.", "left": 227, "right": 402.3 }
          ]
        }
      ]
    }
  ]
}
```

Como ler:

- **`pages[].text`** é exatamente o texto que a estratégia `regex` enxerga (uma linha por linha visual; palavras separadas por **um** espaço; linhas separadas por `\n`). Copie para um testador de regex (ex.: regex101.com, sabor .NET) para montar padrões.
- **`lines[].cells`** mostra os "blocos" da linha. Na linha 2 acima, `CNPJ` e `Razão Social` são células separadas — e o valor de cada uma está **na linha de baixo** → use `labelBelow`.
- **Coordenadas** em *pontos* (1/72 de polegada). Origem no **canto inferior esquerdo** da página: `y` maior = mais alto na página. Uma página A4 tem ~595 × 842.

---

## Templates

### `GET /api/templates`

Lista os templates prontos (`name` + `description`).

### `GET /api/templates/{name}`

Devolve o JSON do template. Ótimo ponto de partida: copie o de um documento parecido e adapte.

### `POST /api/templates/validate`

Valida um template **sem PDF**. Corpo: o JSON do template (`Content-Type: application/json`).

- `200 { "valid": true, "fields": 5 }` — template ok;
- `400` com a lista de problemas (ver [Erros](#erros)).

---

## Amostras

Atalhos que leem os PDFs da pasta `pdf-amostras/` — **sem upload**. Ideais para testar pelo Swagger depois do F5.

| Endpoint | Descrição |
|---|---|
| `GET /api/samples` | Lista os arquivos |
| `GET /api/samples/{fileName}/inspect?pages=&includeWords=&includeBlocks=` | Raio-X |
| `POST /api/samples/{fileName}/extract/{templateName}?debug=` | Extrai com template pronto |
| `POST /api/samples/{fileName}/extract?debug=` | Extrai com o template enviado no **corpo** (JSON) |

O último é o mais útil enquanto você **escreve** um template: cole o JSON no corpo, execute, ajuste, execute de novo.

---

## Formato da resposta de extração

```json
{
  "template": "dcomp",
  "fileName": "dcomp1.pdf",
  "pageCount": 5,
  "success": true,
  "elapsedMs": 36,
  "data": {
    "cnpj": "03.598.524/0001-14",
    "dataCriacao": "2025-09-09",
    "retificador": false,
    "debitos": [
      { "codigoReceita": "0561-07 - IRRF - ...", "principal": 748512.44, "total": 748512.44 },
      { "codigoReceita": "0588-06 - IRRF - ...", "principal": 1840.10, "total": 1840.10 }
    ],
    "totalDebitos": 750352.54
  },
  "warnings": [],
  "trace": [ ... ]   // só com debug=true
}
```

| Propriedade | Descrição |
|---|---|
| `data` | Os dados. **Mesma estrutura dos `fields` do template**: cada campo vira uma propriedade; `group` vira objeto (ou array se `repeat: true`); `table` vira array de objetos |
| `success` | `false` se algum campo com `"required": true` não foi encontrado |
| `warnings` | Avisos: obrigatório ausente, falha de conversão (ex.: texto que não é decimal), coluna de tabela não encontrada… |
| `trace` | (debug) Para cada campo: página, coordenada `y`, texto da linha onde foi achado e valor bruto (`raw`) antes da conversão. Campos não encontrados aparecem com `note` explicando o motivo |

Campos não encontrados saem como `null` (ou `[]` em listas) — o JSON sempre tem todas as propriedades do template.

Tipos na saída:

| `dataType` | Exemplo no PDF | Saída JSON |
|---|---|---|
| `string` (padrão) | `K2I INTERMEDIACAO LTDA.` | `"K2I INTERMEDIACAO LTDA."` |
| `decimal` | `1.234,56` / `R$ 1.234,56` / `9,70%` | `1234.56` / `1234.56` / `9.70` |
| `integer` | `1.000` | `1000` |
| `date` | `31/03/2025` · `01/2025` · `Agosto de 2025` | `"2025-03-31"` · `"2025-01"` · `"2025-08"` |
| `datetime` | `18/03/2025 10:48:02` | `"2025-03-18T10:48:02"` |
| `boolean` | `Sim` / `Não` / `X` | `true` / `false` / `true` |

---

## Erros

Todos os erros seguem o padrão *ProblemDetails* (RFC 7807).

| Status | Quando | Exemplo |
|---|---|---|
| `400` | Arquivo não enviado | `"title": "Arquivo não informado"` |
| `400` | Template inválido (JSON malformado, propriedade desconhecida, regra violada) | ver abaixo |
| `404` | Template salvo ou amostra não existe | `"title": "Template 'xyz' não encontrado"` |
| `422` | O arquivo não pôde ser lido como PDF (corrompido, não é PDF, protegido por senha) | `"detail": "PdfDocumentFormatException: ..."` |

Exemplo de `400` de template — as mensagens indicam **o caminho do campo** e o que corrigir:

```json
{
  "title": "Template inválido",
  "status": 400,
  "errors": {
    "template": [
      "fields.cnpj: informe \"label\" ou \"labelPattern\" para type=labelRight.",
      "fields.total: dataType 'money' inválido. Use: string, decimal, integer, date, datetime, boolean.",
      "fields.total.pattern: regex inválida (Invalid pattern 'Total (' at offset 7. Not enough )'s.)."
    ]
  }
}
```

> **Erro de digitação em propriedade** (ex.: `"lable"` em vez de `"label"`) **não é ignorado** — a API responde 400 dizendo qual propriedade não existe. Isso evita templates que "funcionam pela metade".
