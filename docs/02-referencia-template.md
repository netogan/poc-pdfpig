# 02 – Referência do template (payload de extração)

O template é um JSON que diz **o que extrair** e **como encontrar** cada informação no PDF.
Templates prontos para consulta: [`src/PocPdfPig.Api/Templates/`](../src/PocPdfPig.Api/Templates/).

- [1. Como o motor "enxerga" o PDF](#1-como-o-motor-enxerga-o-pdf)
- [2. Estrutura do template](#2-estrutura-do-template)
- [3. `settings`](#3-settings)
- [4. Propriedades comuns a todos os campos](#4-propriedades-comuns-a-todos-os-campos)
- [5. Tipos de campo (estratégias)](#5-tipos-de-campo-estratégias)
  - [labelRight](#labelright--valor-à-direita-do-rótulo) · [labelBelow](#labelbelow--valor-abaixo-do-rótulo) · [regex](#regex--expressão-regular) · [region](#region--retângulo-da-página) · [table](#table--tabela) · [group](#group--blocos-seções-e-repetições) · [metadata](#metadata--metadados-do-pdf) · [formField](#formfield--campos-de-formulário)
- [6. Como os rótulos são encontrados](#6-como-os-rótulos-são-encontrados)
- [7. Pós-processamento do valor](#7-pós-processamento-do-valor)
- [8. Regex: guia rápido para templates](#8-regex-guia-rápido-para-templates)
- [9. Qual estratégia usar?](#9-qual-estratégia-usar)
- [10. Tabela-resumo de propriedades](#10-tabela-resumo-de-propriedades)

---

## 1. Como o motor "enxerga" o PDF

PDF não tem "campos" nem "tabelas": tem **letras desenhadas em coordenadas**. O PdfPig lê essas letras e agrupa em **palavras**. A POC então organiza as palavras assim:

```
Documento
 └─ Página (número, largura, altura)
     └─ Linha   = palavras na mesma altura (baseline), da esquerda para a direita
         └─ Célula = palavras "coladas" (espaço entre elas ≤ cellGap)
             └─ Palavra (texto + caixa: left, bottom, right, top)
```

Exemplo real (DARF), como aparece em `POST /api/inspect`:

```
linha y=660:  [CNPJ]                         [Razão Social]
linha y=645:        [34.335.332/0001-49]     [K2I INTERMEDIACAO LTDA.]
```

Duas células por linha. O valor de `CNPJ` está **abaixo** do rótulo → `labelBelow`.

E na DCTFWeb:

```
linha y=746:  [Nome do Contribuinte KANGU PARTICIPACOES S.A.]   [CNPJ 32.608.999/0001-42]
```

Rótulo e valor estão **na mesma linha, logo à direita** → `labelRight`.

**Coordenadas:** em pontos (1/72"). Origem no **canto inferior esquerdo**: `y`/`bottom` maiores = mais alto na página. A4 ≈ 595 × 842.

**Ordem de leitura:** todas as linhas do documento formam uma sequência (página 1 de cima para baixo, depois página 2…). Estratégias como `labelBelow`, `group` e `table` andam nessa sequência.

---

## 2. Estrutura do template

```jsonc
{
  "name": "meu-documento",               // opcional, volta no response
  "description": "O que é este template", // opcional
  "settings": { ... },                   // opcional (ver seção 3)
  "fields": [                            // obrigatório: o que extrair
    { "name": "cnpj", "type": "labelRight", "label": "CNPJ:" },
    { "name": "debitos", "type": "group", "start": "^Débito", "repeat": true, "fields": [ ... ] }
  ]
}
```

- O JSON aceita **comentários** (`// ...`) e vírgula sobrando no fim de listas.
- Nomes de propriedades **não** diferenciam maiúsculas (`dataType` = `datatype`), mas **propriedade inexistente é erro** (proteção contra erro de digitação).
- A saída (`data`) tem **exatamente** a forma dos `fields`: cada `name` vira uma propriedade.
- Em JSON, a barra invertida precisa ser duplicada: a regex `\d{2}` é escrita `"\\d{2}"`.

---

## 3. `settings`

| Propriedade | Padrão | Descrição |
|---|---|---|
| `pages` | todas | Páginas a ler: `"1"`, `"1-3"`, `"1,3,5-7"`, `"2-last"` |
| `lineTolerance` | `3` | Diferença vertical máxima (pt) para palavras ficarem na **mesma linha**. Aumente se uma linha "quebra" em duas; diminua se duas linhas viram uma |
| `cellGap` | `8` | Espaço horizontal (pt) acima do qual as palavras viram **células** diferentes. Afeta `take: "cell"`, `labelBelow` e colunas automáticas de `table` |
| `ignoreAccents` | `true` | `Período` casa com `periodo`. Vale para rótulos e **todas** as regex |
| `caseSensitive` | `false` | `CNPJ` casa com `cnpj`. Vale para rótulos e **todas** as regex |
| `ignoreLines` | `[]` | Lista de regex: linhas que casarem são **removidas do documento todo** (cabeçalhos/rodapés repetidos) |
| `wordExtractor` | `"default"` | Algoritmo de palavras do PdfPig: `"default"` ou `"nearestNeighbour"` (ver [doc 03](03-pdfpig-recursos.md)) |
| `culture` | `"pt-BR"` | Cultura para converter números e datas |

---

## 4. Propriedades comuns a todos os campos

| Propriedade | Descrição |
|---|---|
| `name` | **Obrigatório.** Nome da propriedade na saída. Único dentro do mesmo nível |
| `type` | **Obrigatório.** Estratégia: `labelRight`, `labelBelow`, `regex`, `region`, `table`, `group`, `metadata`, `formField` |
| `description` | Comentário livre (ignorado). Use para explicar decisões do template |
| `required` | `true` → se não encontrar, `success` vira `false` e entra um aviso em `warnings` |
| `page` | Limita a busca a uma página (1 = primeira) |
| `dataType`, `format`, `valuePattern`, `transforms`, `nullValues`, `default` | Pós-processamento — ver [seção 7](#7-pós-processamento-do-valor) |

---

## 5. Tipos de campo (estratégias)

### `labelRight` — valor à direita do rótulo

Para layouts **"Rótulo: valor"** na mesma linha.

```json
{ "name": "cnpj", "type": "labelRight", "label": "CNPJ:" }
```

| Propriedade | Padrão | Descrição |
|---|---|---|
| `label` / `labelPattern` | — | Rótulo (texto) ou regex do rótulo. Um dos dois é obrigatório. Ver [seção 6](#6-como-os-rótulos-são-encontrados) |
| `occurrence` | `1` | Qual ocorrência do rótulo: `1`, `2`… ou `-1` (última), `-2` (penúltima) |
| `take` | `"cell"` | `"cell"`: só o **primeiro bloco** de palavras após o rótulo (para no próximo espaço grande). `"line"`: **tudo** à direita até o fim da linha |
| `maxWords` | — | Limita a quantidade de palavras |
| `multiline` | `false` | Concatena as linhas seguintes que continuam o valor (ver abaixo) |

**`take: "cell"` x `"line"`** — linha da NFS-e:

```
Valor dos Serviços   R$   42.245,16   Natureza Operação   ...
```

- `"cell"` → `"R$"` (o primeiro bloco após o rótulo);
- `"line"` + `"valuePattern": "[\\d.]+,\\d{2}"` → `42.245,16` ✅.

**`multiline`** — quando o valor quebra de linha (DCOMP):

```
Nome Empresarial        COCAL COMERCIO INDUSTRIA CANAA ACUCAR E ALCOOL
                        SA
Data de Criação         20/03/2026
```

Com `"multiline": true` o resultado é `COCAL ... ALCOOL SA`. Uma linha é considerada continuação se: está na mesma página, logo abaixo (distância ≤ 1,5× a altura da linha) e **todas** as suas palavras começam na posição do valor ou mais à direita (não tem nada na coluna do rótulo).

**Detalhes:**

- Se o rótulo termina no meio de uma palavra (rótulo `Registro`, palavra `Registro:`), o resto (`:`) é descartado.
- Dois-pontos e espaços no início do valor são removidos.
- Se não houver nada à direita, o valor é `null`.

---

### `labelBelow` — valor abaixo do rótulo

Para formulários em que o rótulo é um "título de caixa" e o valor fica **embaixo** (DARF, GFD, NFS-e).

```json
{ "name": "dataVencimento", "type": "labelBelow", "label": "Data de Vencimento", "dataType": "date" }
```

| Propriedade | Padrão | Descrição |
|---|---|---|
| `label` / `labelPattern`, `occurrence` | — | Como em `labelRight` |
| `lines` | `1` | Quantas linhas (com conteúdo na coluna) compõem o valor |
| `multiline` | `false` | Continua juntando as linhas seguintes enquanto houver conteúdo na coluna |

**Como a "coluna" é definida:** a linha do rótulo é tratada como **cabeçalho**; cada célula dela é uma coluna. Nas linhas de baixo, cada palavra é atribuída a uma coluna assim:

1. a coluna com **maior sobreposição horizontal** com a palavra;
2. se não sobrepõe nenhuma (comum: valores alinhados à direita e em fonte maior que o rótulo), a coluna **cujo início está mais próximo, à esquerda,** do centro da palavra.

```
Período Apuração        Data de Vencimento     Número do Documento      ← cabeçalho (3 colunas)
          31/03/2025          30/04/2025              07012511825430641 ← cada valor cai na sua coluna
```

O motor procura nas linhas seguintes da **mesma página**, pulando até **3 linhas** sem nada na coluna antes de desistir.

> ⚠️ Se um valor de **outro quadro** estiver à direita e na linha de baixo, ele pode "cair" na última coluna. Nesse caso use [`region` relativa ao rótulo](#region--retângulo-da-página) (exemplo real em `guia-fgts-digital.json`).

---

### `regex` — expressão regular

A ferramenta mais flexível. A regex roda sobre o **texto do escopo**: linhas separadas por `\n`, palavras separadas por **um** espaço (é o `pages[].text` do `/api/inspect`).

```json
{ "name": "valorTotal", "type": "regex", "pattern": "Valor Total: R\\$ (?<value>[\\d.]+,\\d{2})", "dataType": "decimal" }
```

| Propriedade | Padrão | Descrição |
|---|---|---|
| `pattern` | — | **Obrigatório.** A regex (sintaxe .NET). `^` e `$` valem **por linha** |
| `group` | — | Nome ou número do grupo a retornar |
| `all` | `false` | `true` retorna **todas** as ocorrências (array) |
| `occurrence` | `1` | Qual ocorrência usar quando `all` é `false` (`-1` = última) |
| `columns` | — | Conversão de cada grupo nomeado (quando há vários grupos; ver abaixo) |

**Qual parte do match vira o valor:**

1. se `group` foi informado → esse grupo;
2. se existe o grupo `(?<value>...)` ou **um único** grupo nomeado → ele;
3. se há **vários** grupos nomeados → um **objeto** `{ grupo: valor }`;
4. senão → o grupo 1 (primeiro parênteses), ou o match inteiro se não houver grupos.

**Vários grupos → objeto** (DARF – linha de totais logo acima de "Totais"):

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

Saída: `"totais": { "principal": 360492.27, "multa": 0.00, "juros": 0.00, "total": 360492.27 }`.

**Lista com `all: true`** (DI – despachantes):

```json
{ "name": "despachantes", "type": "regex", "all": true,
  "pattern": "^(?<nome>[^\\n]+?) - CPF\\. (?<cpf>\\d{3}\\.\\d{3}\\.\\d{3}-\\d{2})$" }
```

Saída: `[ { "nome": "Anderson de Moraes", "cpf": "326.252.598-76" }, ... ]`.

**Texto em várias linhas** (NFS-e – discriminação entre dois títulos):

```json
{ "name": "discriminacao", "type": "regex",
  "pattern": "^Discriminação dos Serviços\\n(?<value>[\\s\\S]*?)\\n^Código do Serviço" }
```

`[\s\S]*?` = "qualquer coisa, inclusive quebra de linha, o mínimo possível". As quebras viram espaço no resultado.

---

### `region` — retângulo da página

Pega as palavras cujo **centro** está dentro de um retângulo. Duas formas:

**Absoluta** (coordenadas da página — descubra com `/api/inspect?includeWords=true`):

```json
{ "name": "numero", "type": "region", "region": { "page": 1, "left": 430, "bottom": 735, "right": 560, "top": 750 } }
```

**Relativa a um rótulo** (mais robusta: acompanha o rótulo se o layout "deslizar"):

```json
{
  "name": "razaoSocialEmpregador",
  "type": "region",
  "label": "Nome/Razão Social do Empregador",
  "region": { "left": 0, "right": 300, "bottom": -16, "top": -4 }
}
```

Com `label`/`labelPattern`, os números são **deslocamentos** a partir do canto inferior esquerdo do rótulo: `left/right` somam ao `left` do rótulo; `bottom/top` somam ao `bottom` do rótulo (negativo = abaixo). No exemplo: de 0 a 300pt à direita do início do rótulo, entre 4 e 16pt abaixo dele.

| Propriedade | Descrição |
|---|---|
| `region.left/bottom/right/top` | Retângulo (exige `right > left` e `top > bottom`) |
| `region.page` | Página (modo absoluto). Padrão: primeira página do escopo |
| `label` / `labelPattern` / `occurrence` | Ativam o modo relativo |

---

### `table` — tabela

Linha de **cabeçalho** + linhas de **dados** abaixo. Cada palavra das linhas de dados é atribuída a uma coluna pela posição horizontal (mesma regra do `labelBelow`).

```json
{
  "name": "pagamentos",
  "type": "table",
  "header": "Data de Arrecadação",
  "rowPattern": "^\\d{2}/\\d{2}/\\d{4}\\s",
  "columns": [
    { "name": "dataArrecadacao", "header": "Data de Arrecadação", "dataType": "date" },
    { "name": "receita", "header": "Receita" },
    { "name": "principal", "header": "Principal (R$)", "dataType": "decimal" },
    { "name": "total", "header": "Total (R$)", "dataType": "decimal" }
  ]
}
```

| Propriedade | Descrição |
|---|---|
| `header` / `headerPattern` | **Um dos dois é obrigatório.** Identifica a linha de cabeçalho (texto contido ou regex) |
| `columns` | Colunas a extrair. Se omitido, **todas** as células do cabeçalho viram colunas (nome = texto do cabeçalho) |
| `rowPattern` | Regex que a linha precisa casar para ser **linha de dados**. Linhas que não casam são ignoradas (subtotais, títulos…) |
| `end` | Regex da linha que **encerra** a tabela |
| `mergeContinuationLines` | `true`: linha que não casa com `rowPattern` é **anexada** à linha anterior (células com texto quebrado) |
| `includeOtherColumns` | `true`: inclui também as células do cabeçalho não declaradas em `columns` |
| `page` | Limita a uma página |

**Cada coluna (`columns[]`):**

| Propriedade | Descrição |
|---|---|
| `name` | Nome na saída. Use `"*"` para definir `dataType`/`format` das colunas automáticas (`includeOtherColumns`) |
| `header` / `headerPattern` | Texto/regex do cabeçalho da coluna na linha de cabeçalho |
| `left` + `right` | **Ou** limites fixos (pt), para colunas sem cabeçalho |
| `dataType`, `format`, `valuePattern`, `transforms`, `nullValues`, `default` | Pós-processamento do valor da célula |

**Comportamentos importantes:**

- **Tabela em várias páginas:** sempre que o cabeçalho reaparece, o motor recalcula as colunas e continua. Nada a configurar.
- **Validação do cabeçalho:** uma linha só é aceita como cabeçalho se tiver **pelo menos metade** das colunas declaradas por `header`. Isso evita confundir com uma linha comum que contenha o mesmo texto (ex.: "Data de arrecadação: 01/01/2008 a ..." no topo da relação de pagamentos).
- **Células não declaradas** no cabeçalho continuam existindo como colunas "anônimas" — assim as palavras abaixo delas não invadem as colunas vizinhas.
- **Colunas dinâmicas** (ex.: meses que mudam a cada trimestre) — `perdcomp-ressarcimento.json`:

```json
{
  "name": "valores",
  "type": "table",
  "headerPattern": "^(Janeiro|Fevereiro|Março|Abril|Maio|Junho|Julho|Agosto|Setembro|Outubro|Novembro|Dezembro)\\b",
  "rowPattern": "\\d,\\d{2}$",
  "includeOtherColumns": true,
  "columns": [
    { "name": "descricao", "left": 0, "right": 260 },
    { "name": "*", "dataType": "decimal" }
  ]
}
```

Saída: `{ "descricao": "Valor do Crédito Apurado", "Abril": 17009.62, "Maio": 6720.33, "Junho": 17448.24, "Total": 41178.19 }`.

---

### `group` — blocos, seções e repetições

O `group` **recorta o documento** em blocos e extrai sub-campos (`fields`) **dentro de cada bloco**. Serve para três coisas:

1. **Rótulos repetidos** em quadros diferentes (o `CNPJ` do prestador x o do tomador);
2. **Itens repetidos** (débitos, DARFs vinculados, adições, comprovantes);
3. **Blocos que atravessam páginas** (com `ignoreLines` removendo cabeçalho/rodapé no meio).

| Propriedade | Padrão | Descrição |
|---|---|---|
| `fields` | — | **Obrigatório.** Campos extraídos dentro de cada bloco (podem ser outros `group`) |
| `start` | — | Regex da linha que **inicia** um bloco. A linha faz parte do bloco |
| `end` | — | Regex da linha que **encerra** o bloco (a linha não entra). Sem `end`, o bloco vai até o próximo `start` (ou o fim do escopo) |
| `perPage` | `false` | Cada página vira um bloco (no lugar de `start`) |
| `repeat` | `false` | `true` → **array** com todos os blocos; `false` → **objeto** com um bloco só |
| `occurrence` | `1` | Com `repeat: false`, qual bloco usar (`-1` = último) |
| `ignoreLines` | — | Regex de linhas descartadas **dentro** do grupo (cabeçalhos/rodapés de página) |
| `page` | — | Limita a uma página |

Sem `start` e sem `perPage`, o bloco é o escopo inteiro (até `end`, se houver).

**Exemplo 1 – quadros com rótulos repetidos** (NFS-e):

```json
{ "name": "prestador", "type": "group", "start": "^Dados do Prestador", "end": "^Dados do Tomador",
  "fields": [ { "name": "cnpjCpf", "type": "labelRight", "label": "CNPJ/CPF" } ] },
{ "name": "tomador", "type": "group", "start": "^Dados do Tomador", "end": "^Discriminação",
  "fields": [ { "name": "cnpjCpf", "type": "labelRight", "label": "CNPJ/CPF" } ] }
```

**Exemplo 2 – um comprovante por página** (DARF):

```json
{ "name": "comprovantes", "type": "group", "perPage": true, "repeat": true, "fields": [ ... ] }
```

**Exemplo 3 – itens que atravessam páginas** (DCOMP):

```json
{
  "name": "debitos",
  "type": "group",
  "start": "^\\d{3}\\. Débito",
  "repeat": true,
  "ignoreLines": [ "^Receita Federal do Brasil$", "^PEDIDO DE RESTITUIÇÃO", "^CNPJ [\\d./-]+ [\\d.-]+$", "^\\d+$" ],
  "fields": [
    { "name": "principal", "type": "labelRight", "labelPattern": "^Principal\\b", "dataType": "decimal" },
    { "name": "total", "type": "labelRight", "labelPattern": "^Total\\b", "dataType": "decimal" }
  ]
}
```

Sem o `ignoreLines`, o cabeçalho da página seguinte apareceria no meio do débito 002 (que começa na página 4 e termina na 5). Com ele, o bloco fica "contínuo".

**Exemplo 4 – grupo dentro de grupo** (DCTF: cada débito tem sua lista de DARFs):

```json
{ "name": "debitos", "type": "group", "start": "^GRUPO DO TRIBUTO:", "repeat": true,
  "fields": [
    { "name": "codigoReceita", "type": "labelRight", "label": "CÓDIGO DA RECEITA:" },
    { "name": "darfs", "type": "group", "start": "^Relação de DARF vinculados", "repeat": true,
      "fields": [ { "name": "valorPago", "type": "labelRight", "label": "Valor Pago do Débito:", "dataType": "decimal" } ] }
  ] }
```

---

### `metadata` — metadados do PDF

```json
{ "name": "produtor", "type": "metadata", "key": "Producer" }
```

`key`: `Title`, `Author`, `Subject`, `Keywords`, `Creator`, `Producer`, `CreationDate`, `ModifiedDate`, `PageCount`, `Version`.

### `formField` — campos de formulário

Para PDFs **preenchíveis** (AcroForm), lê o valor do campo pelo nome interno. Descubra os nomes em `formFields` do `/api/inspect`.

```json
{ "name": "cpf", "type": "formField", "key": "txtCpf" }
```

Caixas de seleção retornam `"true"`/`"false"` (use `"dataType": "boolean"`). Nenhuma das amostras é formulário — os documentos da Receita são PDFs "impressos".

---

## 6. Como os rótulos são encontrados

Vale para `label` em `labelRight`, `labelBelow`, `region`, `table.header` e `columns[].header`:

- busca por **"contém"** dentro da linha (o rótulo pode estar no meio da linha);
- respeita **início/fim de palavra**: `"Total"` **não** casa com `"Subtotal"` nem com `"Totais"`;
- ignora **acentos** e **maiúsculas** (configurável em `settings`) e espaços duplicados;
- pode atravessar células: `"Valor dos Serviços"` casa mesmo que as palavras estejam separadas por espaço grande;
- a busca segue a ordem de leitura; `occurrence` escolhe qual ocorrência usar.

Use **`labelPattern`** (regex) quando o texto simples for ambíguo:

| Situação | `labelPattern` |
|---|---|
| Só no **início** da linha | `"^Total\\b"` |
| Linha que é **só** o rótulo | `"^Periodicidade$"` |
| Rótulo seguido de dígito | `"^CNPJ(?= \\d)"` (lookahead: o dígito não entra no rótulo) |
| Rótulo com variações | `"^Forma de Tributação( do Lucro\| no Período)?"` |

---

## 7. Pós-processamento do valor

Todo valor encontrado passa por este pipeline, **nesta ordem**:

```
texto bruto
  → (não encontrado?) usa "default"
  → espaços duplicados viram um
  → valuePattern   (recorta parte do texto)
  → transforms     (upper, digitsOnly, ...)
  → nullValues     (ex.: "-" vira null) — texto vazio também vira null
  → dataType       (converte para número, data, booleano...)
```

| Propriedade | Descrição |
|---|---|
| `valuePattern` | Regex aplicada ao texto bruto. Usa o grupo `(?<value>...)`, senão o grupo 1, senão o match inteiro. Se não casar → `null` (ou `default`) |
| `transforms` | Lista, aplicada em ordem: `trim`, `upper`, `lower`, `digitsOnly`, `removeSpaces`, `collapseSpaces` |
| `nullValues` | Textos que significam "sem valor". Ex.: `["-", "N/A", "***"]` |
| `default` | Valor (texto) usado quando nada é encontrado. Também passa pela conversão |
| `dataType` | `string` (padrão), `decimal`, `integer`, `date`, `datetime`, `boolean` |
| `format` | Formato de data (vários separados por `\|`) **ou**, para `decimal`, a cultura do número (ex.: `"en-US"` para `1,234.56`) |

**Conversões:**

| `dataType` | Aceita | Saída |
|---|---|---|
| `decimal` | `1.234,56`, `R$ 1.234,56`, `9,70%`, `(1.234,56)` = negativo | número JSON `1234.56` |
| `integer` | `2025`, `1.000` | número JSON |
| `date` | `dd/MM/yyyy`, `d/M/yyyy`, `dd/MM/yy`, `dd/MM/yyyy HH:mm:ss`, `MM/yyyy`, `Agosto de 2025`, `Janeiro / 2019`, `JAN 2019`… | `"yyyy-MM-dd"`; se o formato não tem dia → `"yyyy-MM"` |
| `datetime` | os mesmos | `"yyyy-MM-ddTHH:mm:ss"` |
| `boolean` | `Sim`, `S`, `X`, `(X)`, `true`, `1` / `Não`, `N`, `false`, `0` | `true` / `false` |

Formatos de data personalizados usam o padrão .NET: `dd` dia, `MM` mês, `MMM` mês abreviado (`JAN`), `MMMM` mês por extenso (`Janeiro`), `yyyy` ano, `HH:mm:ss` hora. Texto fixo entre aspas simples: `"dd/MM/yyyy 'às' HH:mm:ss"`.

Se a conversão falhar, o campo fica `null` e aparece um aviso em `warnings` explicando (ex.: `debitos[0].valor: não foi possível converter 'abc' para decimal (pt-BR)`).

---

## 8. Regex: guia rápido para templates

As regex usam a sintaxe do .NET. Teste em <https://regex101.com> (escolha o sabor **.NET**) colando o `pages[].text` do `/api/inspect`.

| Padrão | Significa | Exemplo |
|---|---|---|
| `\d` | um dígito | `\d{2}/\d{2}/\d{4}` → `31/03/2025` |
| `[\d.]+,\d{2}` | valor monetário brasileiro | `1.234.567,89` |
| `\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}` | CNPJ | `34.335.332/0001-49` |
| `\d{3}\.\d{3}\.\d{3}-\d{2}` | CPF | `279.918.188-02` |
| `^` / `$` | início / fim da **linha** | `^Total` |
| `\b` | limite de palavra | `^Total\b` não casa `Totais` |
| `.+` / `.+?` | qualquer coisa (guloso / o mínimo possível) | |
| `[\s\S]*?` | qualquer coisa **incluindo quebras de linha** | blocos de texto |
| `\n` | quebra de linha (próxima linha visual) | `Período Apuração\n(?<value>\d{2}/\d{4})` |
| `(?<value>...)` | grupo nomeado: o valor a retornar | |
| `(?=...)` | lookahead: "seguido de", sem consumir | `^CNPJ(?= \d)` |
| `a\|b` | alternativa | `(Sim\|Não)` |
| `(?-i)` | liga diferenciação de maiúsculas **só nesta regex** | `(?-i)^CRÉDITO ` não casa `Crédito Oriundo...` |
| `\.` `\$` `\(` `\)` `\/` | caracteres especiais escapados | `R\$ ` |

**Lembretes:**

1. No JSON, **duplique** a barra invertida: `\d` → `"\\d"`, `\.` → `"\\."`.
2. Com `ignoreAccents: true` (padrão) pode escrever com ou sem acento: `Período` = `Periodo`. O valor retornado mantém os acentos originais.
3. Entre palavras há **sempre um espaço só**, mesmo que no PDF pareça haver um espaço grande.

---

## 9. Qual estratégia usar?

| O que você vê no PDF | Estratégia |
|---|---|
| `Rótulo: valor` na mesma linha | `labelRight` |
| Rótulo como título de uma caixa, valor embaixo | `labelBelow` |
| Valor com formato característico (CNPJ, nº de documento, data/hora) em qualquer lugar | `regex` |
| Valor entre dois textos fixos, ou em várias linhas | `regex` com `[\s\S]*?` |
| Vários valores na mesma linha, sem rótulos próprios | `regex` com vários grupos nomeados |
| Tabela com cabeçalho | `table` |
| Lista de linhas com padrão fixo, sem cabeçalho | `regex` com `all: true` |
| O mesmo rótulo em quadros diferentes | `group` com `start`/`end` para cada quadro |
| Itens repetidos (débitos, adições, comprovantes) | `group` com `repeat: true` |
| Um documento por página | `group` com `perPage: true, repeat: true` |
| Valor sem rótulo útil, sempre na mesma posição | `region` (absoluta) |
| Valor perto de um rótulo, mas "atrapalhado" por outros textos | `region` relativa ao `label` |
| PDF formulário preenchível | `formField` |

---

## 10. Tabela-resumo de propriedades

| Propriedade | labelRight | labelBelow | regex | region | table | group |
|---|:-:|:-:|:-:|:-:|:-:|:-:|
| `label` / `labelPattern` | ● | ● | | ○ | | |
| `occurrence` | ○ | ○ | ○ | ○ | | ○ |
| `page` | ○ | ○ | ○ | ○ | ○ | ○ |
| `take`, `maxWords` | ○ | | | | | |
| `multiline` | ○ | ○ | | | | |
| `lines` | | ○ | | | | |
| `pattern` | | | ● | | | |
| `group`, `all` | | | ○ | | | |
| `columns` | | | ○ | | ○ | |
| `region` | | | | ● | | |
| `header` / `headerPattern` | | | | | ● | |
| `rowPattern`, `mergeContinuationLines`, `includeOtherColumns` | | | | | ○ | |
| `start`, `perPage`, `repeat`, `ignoreLines`, `fields` | | | | | | ● / ○ |
| `end` | | | | | ○ | ○ |
| pós-processamento (seção 7) | ○ | ○ | ○ | ○ | por coluna | |

● obrigatório (ou um dos marcados) · ○ opcional
