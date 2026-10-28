# 04 – Passo a passo: criando o template de um documento novo

Este tutorial mostra, do zero, como montar o template de um documento. Usaremos o **Comprovante de Arrecadação do DARF** (`pdf-amostras/darf-irpj.pdf`) porque ele tem um pouco de tudo: rótulos acima dos valores, uma tabela e um comprovante por página.

> Pré-requisito: API rodando (F5 — veja o [README](../README.md)) e o Swagger aberto.

- [Passo 0 – O ciclo de trabalho](#passo-0--o-ciclo-de-trabalho)
- [Passo 1 – Faça o raio-X do PDF](#passo-1--faça-o-raio-x-do-pdf)
- [Passo 2 – Liste o que precisa ser extraído](#passo-2--liste-o-que-precisa-ser-extraído)
- [Passo 3 – Primeiro campo](#passo-3--primeiro-campo)
- [Passo 4 – Demais campos simples](#passo-4--demais-campos-simples)
- [Passo 5 – A tabela](#passo-5--a-tabela)
- [Passo 6 – Vários comprovantes no mesmo PDF](#passo-6--vários-comprovantes-no-mesmo-pdf)
- [Passo 7 – Tipos, obrigatórios e debug](#passo-7--tipos-obrigatórios-e-debug)
- [Passo 8 – Salvar e testar](#passo-8--salvar-e-testar)
- [Checklist](#checklist)
- [Solução de problemas](#solução-de-problemas)

---

## Passo 0 – O ciclo de trabalho

```
 ┌──────────────┐    ┌───────────────────┐    ┌──────────────────────┐    ┌──────────────┐
 │ 1. Inspecionar│ ─► │ 2. Escrever/ajustar│ ─► │ 3. Testar (debug=true)│ ─► │ 4. Conferir   │
 │  /api/inspect │    │    o template     │    │  .../extract          │    │  data/trace  │
 └──────────────┘    └───────────────────┘    └──────────────────────┘    └──────┬───────┘
                               ▲                                                   │
                               └─────────────────── repetir ◄──────────────────────┘
```

Endpoints que você vai usar:

| Etapa | Com o PDF em `pdf-amostras/` | Com upload |
|---|---|---|
| Inspecionar | `GET /api/samples/{arquivo}/inspect` | `POST /api/inspect` |
| Testar template | `POST /api/samples/{arquivo}/extract?debug=true` (template no corpo) | `POST /api/extraction` (campos `file`, `template`, `debug`) |
| Validar sintaxe | `POST /api/templates/validate` | |

> **Dica:** para um documento novo, copie o PDF para `pdf-amostras/` e use os endpoints `/api/samples` — é bem mais rápido do que fazer upload a cada teste.

---

## Passo 1 – Faça o raio-X do PDF

`GET /api/samples/darf-irpj.pdf/inspect?pages=1`

Olhe primeiro o `pages[0].text` (resumido):

```
Comprovante de Arrecadação
Comprovamos que consta nos sistemas da Receita Federal registro de arrecadação de DARF com os dados a seguir:
CNPJ Razão Social
34.335.332/0001-49 K2I INTERMEDIACAO LTDA.
Período Apuração Data de Vencimento Número do Documento
31/03/2025 30/04/2025 07012511825430641
Composição do Documento de Arrecadação
Código Descrição Principal Multa Juros Total
2089 IRPJ - Lucro presumido 360.492,27 - - 360.492,27
01 - IRPJ - LUCRO PRESUMIDO
360.492,27 0,00 0,00 360.492,27
Totais
Banco Data de Arrecadação
033 - BANCO SANTANDER MERIDIONAL S/A 29/04/2025
Agência Estabelecimento Valor Reservado/Restituído Referência
0285 0653 0,00
Comprovante emitido às 10:48:19 de 26/03/2026 (horário de Brasília), sob o código de controle
c373.7eef.3e63.ce0d.acad.1cfb.1854.50be
...
```

O que dá para concluir:

1. **Linhas de rótulos e linhas de valores se alternam** (`CNPJ Razão Social` / `34.335... K2I...`) → os valores ficam **abaixo** dos rótulos → `labelBelow`.
2. Existe uma **tabela** com cabeçalho `Código Descrição Principal Multa Juros Total` → `table`.
3. A linha de **totais** vem **antes** da palavra `Totais` e não tem rótulos próprios → `regex`.
4. O código de controle está na linha **seguinte** a "código de controle" → `regex` com `\n`.

Agora olhe `lines[].cells` das linhas de rótulo para confirmar as colunas:

```json
{ "text": "CNPJ Razão Social",
  "cells": [ { "text": "CNPJ", "left": 22, "right": 37.7 },
             { "text": "Razão Social", "left": 227, "right": 262.3 } ] }
{ "text": "34.335.332/0001-49 K2I INTERMEDIACAO LTDA.",
  "cells": [ { "text": "34.335.332/0001-49", "left": 101.7, "right": 218 },
             { "text": "K2I INTERMEDIACAO LTDA.", "left": 227, "right": 402.3 } ] }
```

Duas células em cada linha, alinhadas — perfeito para `labelBelow`.

Por fim, `pageCount` é 4: confira as outras páginas (`pages=2-4`) — cada uma é **outro comprovante** com o mesmo layout.

---

## Passo 2 – Liste o que precisa ser extraído

Antes de escrever JSON, escreva a lista (e o nome que cada campo terá na saída — use *camelCase*, sem acentos):

| Campo | Onde está | Estratégia |
|---|---|---|
| `cnpj` | abaixo de "CNPJ" | `labelBelow` |
| `razaoSocial` | abaixo de "Razão Social" | `labelBelow` |
| `periodoApuracao` | abaixo de "Período Apuração" | `labelBelow` + `date` |
| `numeroDocumento` | abaixo de "Número do Documento" | `labelBelow` |
| `composicao` | tabela | `table` |
| `totais` | linha antes de "Totais" | `regex` (vários grupos) |
| `dataArrecadacao` | abaixo de "Data de Arrecadação" | `labelBelow` + `date` |
| `codigoControle` | linha após "código de controle" | `regex` |

---

## Passo 3 – Primeiro campo

Em `POST /api/samples/{fileName}/extract` informe `fileName = darf-irpj.pdf`, `debug = true` e no corpo:

```json
{
  "name": "darf-tutorial",
  "fields": [
    { "name": "cnpj", "type": "labelBelow", "label": "CNPJ" }
  ]
}
```

Resposta:

```json
{
  "success": true,
  "data": { "cnpj": "34.335.332/0001-49" },
  "warnings": [],
  "trace": [
    { "field": "cnpj", "type": "labelBelow", "page": 1, "y": 644.8,
      "line": "34.335.332/0001-49 K2I INTERMEDIACAO LTDA.", "raw": "34.335.332/0001-49" }
  ]
}
```

O `trace` mostra **onde** o valor foi achado (página 1, linha y=644.8) e o texto bruto. Sempre confira o `trace` quando algo vier diferente do esperado.

---

## Passo 4 – Demais campos simples

```json
{
  "name": "darf-tutorial",
  "fields": [
    { "name": "cnpj", "type": "labelBelow", "label": "CNPJ" },
    { "name": "razaoSocial", "type": "labelBelow", "label": "Razão Social" },
    { "name": "periodoApuracao", "type": "labelBelow", "label": "Período Apuração", "dataType": "date" },
    { "name": "numeroDocumento", "type": "labelBelow", "label": "Número do Documento" },
    { "name": "dataArrecadacao", "type": "labelBelow", "label": "Data de Arrecadação", "dataType": "date" },
    {
      "name": "codigoControle",
      "type": "regex",
      "pattern": "código de controle\\n(?<value>[0-9a-f.]+)"
    }
  ]
}
```

Observe:

- `"dataType": "date"` converte `31/03/2025` em `"2025-03-31"`;
- na regex, `\\n` (escrito com barra dupla no JSON) é a **quebra de linha**: o código está na linha seguinte;
- `(?<value>...)` marca a parte que vira o valor.

---

## Passo 5 – A tabela

```json
{
  "name": "composicao",
  "type": "table",
  "header": "Descrição",
  "rowPattern": "^\\d{4}\\s",
  "end": "^Totais",
  "columns": [
    { "name": "codigo", "header": "Código" },
    { "name": "descricao", "header": "Descrição" },
    { "name": "principal", "header": "Principal", "dataType": "decimal", "nullValues": ["-"] },
    { "name": "multa", "header": "Multa", "dataType": "decimal", "nullValues": ["-"] },
    { "name": "juros", "header": "Juros", "dataType": "decimal", "nullValues": ["-"] },
    { "name": "total", "header": "Total", "dataType": "decimal", "nullValues": ["-"] }
  ]
}
```

Por que cada propriedade:

| Propriedade | Motivo |
|---|---|
| `header: "Descrição"` | Identifica a linha de cabeçalho (palavra que só aparece nela) |
| `rowPattern: "^\\d{4}\\s"` | Só são dados as linhas que começam com o código de 4 dígitos. Isso descarta `01 - IRPJ - LUCRO PRESUMIDO` (continuação da descrição) e a linha de totais |
| `end: "^Totais"` | Para de procurar linhas ao chegar em "Totais" |
| `nullValues: ["-"]` | O DARF mostra `-` quando não há multa/juros; vira `null` em vez de erro de conversão |

Resultado:

```json
"composicao": [
  { "codigo": "2089", "descricao": "IRPJ - Lucro presumido", "principal": 360492.27, "multa": null, "juros": null, "total": 360492.27 }
]
```

E a linha de totais (sem rótulos) com uma regex de **vários grupos nomeados** — cada grupo vira uma propriedade, e `columns` define a conversão de cada uma:

```json
{
  "name": "totais",
  "type": "regex",
  "pattern": "^(?<principal>[\\d.]+,\\d{2}) (?<multa>[\\d.]+,\\d{2}) (?<juros>[\\d.]+,\\d{2}) (?<total>[\\d.]+,\\d{2})\\n^Totais",
  "columns": [
    { "name": "principal", "dataType": "decimal" },
    { "name": "multa", "dataType": "decimal" },
    { "name": "juros", "dataType": "decimal" },
    { "name": "total", "dataType": "decimal" }
  ]
}
```

---

## Passo 6 – Vários comprovantes no mesmo PDF

Até aqui o template só lê o **primeiro** comprovante (a primeira ocorrência de cada rótulo). Como cada página é um comprovante, envolva os campos em um `group` com `perPage` + `repeat`:

```json
{
  "name": "darf-tutorial",
  "fields": [
    {
      "name": "comprovantes",
      "type": "group",
      "perPage": true,
      "repeat": true,
      "fields": [
        { "name": "cnpj", "type": "labelBelow", "label": "CNPJ" },
        { "name": "numeroDocumento", "type": "labelBelow", "label": "Número do Documento" }
        // ... demais campos dos passos 4 e 5
      ]
    }
  ]
}
```

Agora `data.comprovantes` é um array com 4 itens — um por página.

> Se os itens repetidos **não** forem um por página (ex.: débitos de uma DCOMP, que começam no meio da página), use `"start": "^\\d{3}\\. Débito"` no lugar de `perPage`. Veja `Templates/dcomp.json`.

---

## Passo 7 – Tipos, obrigatórios e debug

- Marque como `"required": true` os campos sem os quais o resultado não serve (ex.: `cnpj`, `numeroDocumento`). Se faltarem, `success` vem `false` e `warnings` diz qual.
- Confira `warnings` a cada teste: falhas de conversão aparecem ali (ex.: `não foi possível converter '-' para decimal`).
- Use o `trace` (`debug=true`) para descobrir de onde veio um valor errado.

---

## Passo 8 – Salvar e testar

1. Salve o arquivo em `src/PocPdfPig.Api/Templates/<nome-do-template>.json` (nome em minúsculas, com hífens).
2. Ele aparece em `GET /api/templates` e pode ser usado em `POST /api/extraction/<nome-do-template>` — **sem reiniciar a API**.
3. Teste com **todos os PDFs de exemplo** que você tiver do mesmo tipo (layouts mudam entre emissores, versões e meses!).
4. Adicione um teste em `tests/PocPdfPig.Tests/SampleExtractionTests.cs` com alguns valores-chave e rode `dotnet test`.

O resultado final deste tutorial é o template [`darf-comprovante-arrecadacao.json`](../src/PocPdfPig.Api/Templates/darf-comprovante-arrecadacao.json).

---

## Checklist

- [ ] Rodei o `/inspect` e li o `text` de **todas** as páginas.
- [ ] Para cada campo, identifiquei: o valor está à direita, abaixo, numa tabela ou tem formato próprio?
- [ ] Rótulos que se repetem estão dentro de um `group` (ou usam `occurrence`/`labelPattern`).
- [ ] Itens repetidos usam `group` com `repeat: true`.
- [ ] Cabeçalhos/rodapés de página que "quebram" blocos estão em `ignoreLines`.
- [ ] Números com `"dataType": "decimal"`, datas com `"date"`, Sim/Não com `"boolean"`.
- [ ] Campos essenciais com `"required": true`.
- [ ] Testei com `debug=true` e conferi o `trace`.
- [ ] `warnings` está vazio.
- [ ] Testei com mais de um PDF do mesmo tipo.

---

## Solução de problemas

| Sintoma | Causa provável | Solução |
|---|---|---|
| Campo `null`, `trace` diz "rótulo não encontrado" | Texto do rótulo diferente do PDF (abreviação, dois-pontos, quebra de linha no meio do rótulo) | Copie o rótulo do `text` do `/inspect`. Se o rótulo estiver partido em 2 linhas, use só a parte de uma linha ou mude para `regex` |
| Valor de **outro** campo | O rótulo aparece antes em outro lugar (ex.: "CNPJ" no cabeçalho) | `occurrence`, `labelPattern` com `^`, ou um `group` delimitando o quadro |
| `labelRight` trouxe o **próximo rótulo** | Não há valor à direita; o valor está **abaixo** | Use `labelBelow` |
| `labelRight` trouxe só parte do valor | Valor separado por espaço grande (virou outra célula) | `"take": "line"` (+ `valuePattern` se precisar recortar) |
| `labelRight` trouxe valor + texto extra | Rótulo e valor seguintes estão "colados" | `valuePattern` (ex.: `"^\\S+"` = primeira palavra), `maxWords` ou `"take": "cell"` |
| `labelBelow` trouxe texto de outro quadro | Algo à direita, abaixo, caiu na "coluna" | `region` relativa ao `label` (ver `guia-fgts-digital.json`) |
| Valor quebrado em 2 linhas veio pela metade | | `"multiline": true` |
| Tabela vazia | Cabeçalho não reconhecido, ou `rowPattern` não casa | Veja `warnings`; teste o `rowPattern` no regex101 contra o `text` |
| Tabela com colunas trocadas | Valores alinhados à direita "invadindo" a coluna vizinha | Confira as coordenadas dos cabeçalhos no `/inspect`; use `left`/`right` fixos na coluna |
| Bloco do `group` cortado na virada de página | Cabeçalho/rodapé da página entrou no meio | `ignoreLines` no `group` |
| `group` (sem `repeat`) vazio | `start` casou com uma linha anterior (ex.: título em maiúsculas igual ao do quadro) | Deixe o `start` mais específico, ou use `(?-i)` para exigir maiúsculas |
| Data/número `null` com aviso de conversão | Texto com lixo (ex.: `R$`, `Kg`, hora junto) | `valuePattern` para isolar o número/data, ou `format` |
| "JSON inválido: The JSON property 'x' could not be mapped" | Erro de digitação no nome da propriedade | Corrija conforme a [referência](02-referencia-template.md) |
| `422` ao enviar o PDF | Arquivo não é PDF, está corrompido ou tem senha | Verifique o arquivo |
| Nada é extraído e `letterCount = 0` no `/inspect` | PDF escaneado (imagem) | Precisa de OCR antes — ver [doc 03](03-pdfpig-recursos.md#3-limitações-importantes) |
