# 03 – Recursos do PdfPig (e como a POC os usa)

[PdfPig](https://github.com/UglyToad/PdfPig) (pacote NuGet `PdfPig`, versão usada: **0.1.16**) é uma biblioteca .NET, open source (Apache 2.0), para **ler** e **criar** PDFs. Ela é a "base" da POC: toda informação que o motor de extração usa vem dela.

- [1. O que o PdfPig oferece](#1-o-que-o-pdfpig-oferece)
- [2. Como a POC usa cada recurso](#2-como-a-poc-usa-cada-recurso)
- [3. Limitações importantes](#3-limitações-importantes)
- [4. Exemplos de código com o PdfPig puro](#4-exemplos-de-código-com-o-pdfpig-puro)
- [5. Como estender a POC](#5-como-estender-a-poc)

---

## 1. O que o PdfPig oferece

### 1.1 Abrir o documento

```csharp
using UglyToad.PdfPig;

using var doc = PdfDocument.Open("arquivo.pdf");      // também aceita byte[] ou Stream
using var protegido = PdfDocument.Open("arquivo.pdf", new ParsingOptions { Password = "123" });
```

| `ParsingOptions` | Para quê |
|---|---|
| `Password` / `Passwords` | Abrir PDF protegido por senha |
| `UseLenientParsing` | Tolerar PDFs "mal formados" (padrão: ligado) |
| `SkipMissingFonts` | Não falhar se uma fonte estiver ausente |
| `ClipPaths` | Recortar caminhos vetoriais pela área visível |

### 1.2 Nível do documento

| Recurso | API | Observação |
|---|---|---|
| Nº de páginas | `doc.NumberOfPages` | |
| Metadados | `doc.Information` (`Title`, `Author`, `Creator`, `Producer`, `CreationDate`…) | Exposto pela POC no tipo `metadata` |
| Versão do PDF | `doc.Version` | |
| Criptografia | `doc.IsEncrypted` | |
| Formulário (AcroForm) | `doc.TryGetForm(out var form)` → `form.Fields` (`AcroTextField.Value`, `AcroCheckboxField.IsChecked`, `AcroComboBoxField`…) | Exposto pela POC no tipo `formField` |
| Marcadores (índice) | `doc.TryGetBookmarks(out var bookmarks)` | |
| Metadados XMP | `doc.TryGetXmpMetadata(out var xmp)` | |
| Acesso avançado | `doc.Advanced` | Estruturas internas (ex.: arquivos anexados) |

### 1.3 Nível da página

| Recurso | API | O que traz |
|---|---|---|
| Dimensões | `page.Width`, `page.Height`, `page.Rotation` | Em pontos (1/72") |
| Texto "cru" | `page.Text` | Todas as letras concatenadas, **sem** quebras de linha confiáveis |
| **Letras** | `page.Letters` | Cada caractere com `Value`, `GlyphRectangle` (caixa), `StartBaseLine`, `FontName`, `FontSize`/`PointSize`, `Color`, `TextOrientation` |
| **Palavras** | `page.GetWords()` | Letras agrupadas em palavras, cada uma com `Text` e `BoundingBox` (`Left`, `Bottom`, `Right`, `Top`) |
| Imagens | `page.GetImages()` | `Bounds`, `RawBytes`, `TryGetPng(out bytes)` |
| Anotações | `page.GetAnnotations()` | Comentários, links, carimbos… |
| Hyperlinks | `page.GetHyperlinks()` | Texto + URL |
| Desenhos vetoriais | `page.Paths` | Linhas e retângulos (ex.: **bordas de tabelas**) |
| Conteúdo marcado | `page.GetMarkedContents()` | Estrutura de PDFs "tagueados" (acessibilidade) |

> **Sistema de coordenadas:** origem no **canto inferior esquerdo**. `Bottom` < `Top`. Quanto maior o `y`, mais alto na página.

### 1.4 Análise de layout (`UglyToad.PdfPig.DocumentLayoutAnalysis`)

Algoritmos que transformam letras soltas em estrutura. Já vêm no pacote `PdfPig`.

| Categoria | Classe | Para quê |
|---|---|---|
| **Extratores de palavras** | `DefaultWordExtractor` (usado por `GetWords()`) | Agrupa letras por espaçamento. Funciona bem na maioria dos PDFs gerados por sistemas |
| | `NearestNeighbourWordExtractor` | Agrupa letras pela vizinhança. Melhor para PDFs com espaçamento irregular, texto rotacionado ou letras fora de ordem |
| **Segmentação de página** (blocos/parágrafos) | `DocstrumBoundingBoxes` | Detecta blocos de texto por distância/ângulo entre palavras. Bom para layouts com colunas |
| | `RecursiveXYCut` | Divide a página recursivamente por faixas vazias horizontais/verticais |
| | `DefaultPageSegmenter` | Uma linha por bloco (simples) |
| **Ordem de leitura** | `UnsupervisedReadingOrderDetector`, `RenderingReadingOrderDetector`, `DefaultReadingOrderDetector` | Ordenam os blocos na ordem em que um humano leria |
| **Extração de texto** | `ContentOrderTextExtractor.GetText(page)` | Texto na ordem em que foi escrito no arquivo, com quebras de linha |
| **Exportação** | `AltoXmlTextExporter`, `HOcrTextExporter`, `PageXmlTextExporter`, `SvgTextExporter` | Exporta o layout em formatos padrão de OCR/digitalização ou SVG |

### 1.5 Escrita

| Recurso | API |
|---|---|
| Criar PDF | `PdfDocumentBuilder` → `AddPage(...)`, `AddText(...)`, `Build()` |
| Copiar páginas de outro PDF | `builder.AddPage(outroDocumento, numeroPagina)` |
| Fontes | `AddStandard14Font(...)`, `AddTrueTypeFont(bytes)` |

(Não usado na POC, mas útil para gerar PDFs de teste.)

---

## 2. Como a POC usa cada recurso

```
PDF ──PdfDocument.Open──► páginas ──page.GetWords()──► palavras (texto + caixa)
                                                           │
                         PdfLayoutReader (POC)             ▼
                         agrupa por baseline ──► linhas ──► células (espaço > cellGap)
                                                           │
                         ExtractionEngine (POC)            ▼
                         labelRight / labelBelow / regex / region / table / group
```

| Funcionalidade da POC | Recurso do PdfPig | Onde no código |
|---|---|---|
| Palavras e coordenadas | `page.GetWords()` / `NearestNeighbourWordExtractor` (`settings.wordExtractor`) | `Extraction/PdfLayoutReader.cs` |
| Agrupar em linhas | `Letter.StartBaseLine` (baseline da 1ª letra de cada palavra) | `PdfLayoutReader.BuildLines` |
| Células, colunas, tabelas, rótulos | `Word.BoundingBox` (lógica própria da POC) | `Extraction/ExtractionEngine.cs` |
| Tipo `metadata` | `doc.Information`, `doc.Version`, `doc.NumberOfPages` | `PdfLayoutReader.ReadMetadata` |
| Tipo `formField` | `doc.TryGetForm` + `AcroTextField`/`AcroCheckboxField`/… | `PdfLayoutReader.ReadFormFields` |
| `/api/inspect` → `letterCount`, `imageCount` | `page.Letters`, `page.GetImages()` | `Services/PdfExtractionService.cs` |
| `/api/inspect?includeBlocks=true` | `DocstrumBoundingBoxes.Instance.GetBlocks(words)` | idem |
| `/api/inspect?includeContentOrderText=true` | `ContentOrderTextExtractor.GetText(page)` | idem |

**Por que a POC monta linhas/células em vez de usar `page.Text`?** Porque `page.Text` perde a geometria: não dá para saber o que está "à direita" ou "abaixo" de um rótulo. Com as caixas das palavras, a POC reconstrói a estrutura visual do documento — e todos os documentos fiscais são, essencialmente, formulários visuais.

---

## 3. Limitações importantes

| Limitação | Consequência | O que fazer |
|---|---|---|
| **Não faz OCR** | PDF escaneado (foto/imagem) não tem letras: `letterCount = 0` e `imageCount > 0` no `/api/inspect`. Nada é extraído | Passar por um OCR antes (ex.: Tesseract, Azure Document Intelligence, AWS Textract) |
| **Não detecta tabelas sozinho** | O PdfPig entrega palavras; "tabela" é uma interpretação | A POC implementa `table` com base na posição do cabeçalho. Bibliotecas como *Tabula Sharp* (construída sobre o PdfPig) usam as linhas vetoriais (`page.Paths`) |
| **Não renderiza** | Não gera imagem da página | Usar outra lib (ex.: Docnet, PDFium) se precisar de imagem |
| **Fontes sem mapa Unicode** | Alguns PDFs exibem o texto certo na tela mas o PdfPig lê caracteres "estranhos" | Testar `UseActualText`, ou recorrer a OCR |
| **Texto como desenho** | Alguns geradores desenham o texto como curvas | Igual a escaneado: OCR |
| **PDF protegido** | Sem senha, `Open` falha (a API devolve 422) | Informar `ParsingOptions.Password` |
| **Ordem do texto** | A ordem no arquivo pode não ser a ordem visual | A POC ordena por posição (de cima para baixo, da esquerda para a direita) |

**Diagnóstico rápido no `/api/inspect`:**

- `letterCount = 0` → escaneado → OCR;
- texto com caracteres trocados (`Ã©` no lugar de `é`, símbolos aleatórios) → problema de fonte;
- uma linha visual partida em duas → aumente `lineTolerance`;
- palavras "grudadas" ou partidas → tente `"wordExtractor": "nearestNeighbour"`;
- rótulo e valor na mesma célula quando deveriam estar separados (ou o contrário) → ajuste `cellGap`.

---

## 4. Exemplos de código com o PdfPig puro

**Listar palavras com coordenadas:**

```csharp
using UglyToad.PdfPig;

using var doc = PdfDocument.Open("pdf-amostras/darf-irpj.pdf");
foreach (var page in doc.GetPages())
foreach (var word in page.GetWords())
    Console.WriteLine($"p{page.Number} [{word.BoundingBox.Left:F0},{word.BoundingBox.Bottom:F0}] {word.Text}");
```

**Fonte e tamanho de cada letra** (útil para achar títulos/negrito):

```csharp
foreach (var letter in page.Letters)
    Console.WriteLine($"{letter.Value} {letter.FontName} {letter.PointSize}");
```

**Blocos de texto (Docstrum) e ordem de leitura:**

```csharp
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;

var words = page.GetWords();
var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
var ordered = UnsupervisedReadingOrderDetector.Instance.Get(blocks);
foreach (var block in ordered)
    Console.WriteLine($"--- bloco {block.ReadingOrder}\n{block.Text}");
```

**Extrator de palavras alternativo:**

```csharp
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
var words = page.GetWords(NearestNeighbourWordExtractor.Instance);
```

**Campos de formulário:**

```csharp
using UglyToad.PdfPig.AcroForms.Fields;

if (doc.TryGetForm(out var form))
    foreach (var field in form.Fields)
        if (field is AcroTextField text)
            Console.WriteLine($"{text.Information.PartialName} = {text.Value}");
```

**Linhas vetoriais (bordas de tabela):**

```csharp
foreach (var path in page.Paths)
    Console.WriteLine($"path: {path.GetBoundingRectangle()}");
```

---

## 5. Como estender a POC

### Adicionar uma nova estratégia (`type`)

1. **Modelo** — se precisar de propriedades novas, adicione em `Models/ExtractionTemplate.cs` (`FieldDefinition`), com comentário XML (aparece no Swagger).
2. **Validação** — inclua o nome em `TemplateParser.FieldTypes` e as regras no `switch` de `TemplateParser.ValidateFields` (`Services/TemplateParser.cs`).
3. **Execução** — adicione o `case` em `ExtractionEngine.Evaluate` e implemente o método. Use os utilitários existentes:
   - `FindLabel(field, scope)` — acha o rótulo (respeita `label`, `labelPattern`, `occurrence`, `page`);
   - `GetRegex(pattern, multiline)` — cria regex já com as opções de acento/maiúsculas;
   - `Finish(field, raw, path, line)` — aplica o pós-processamento e registra o `trace`;
   - `NotFound(field, path, motivo)` — quando não achar.
4. **Teste** — adicione um caso em `tests/PocPdfPig.Tests/SampleExtractionTests.cs`.
5. **Documente** — em [02-referencia-template.md](02-referencia-template.md).

### Ideias de evolução

| Ideia | Recurso do PdfPig |
|---|---|
| Detectar negrito/títulos para delimitar seções | `Letter.FontName` (`...Bold`), `Letter.PointSize` |
| Tabelas por bordas desenhadas | `page.Paths` |
| Documentos em colunas (jornal, artigos) | `DocstrumBoundingBoxes` + `UnsupervisedReadingOrderDetector` |
| Extrair QR Code/código de barras | `page.GetImages()` + lib de leitura de códigos |
| Suporte a escaneados | OCR antes do PdfPig; o resto do motor continua igual se o OCR gerar um PDF com texto |
| Identificar o tipo de documento automaticamente | Rodar regex "assinatura" (ex.: `DCTFWeb`, `PERDCOMP`) e escolher o template |
