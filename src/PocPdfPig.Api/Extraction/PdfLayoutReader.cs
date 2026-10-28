using System.Text;
using PocPdfPig.Api.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace PocPdfPig.Api.Extraction;

/// <summary>
/// Lê o PDF com o PdfPig e monta o <see cref="PdfLayout"/>:
/// 1. page.GetWords() → palavras com caixa delimitadora;
/// 2. agrupa palavras pela linha de base (baseline) → linhas;
/// 3. dentro de cada linha, separa "células" quando o espaço entre palavras é maior que CellGap.
/// </summary>
public static class PdfLayoutReader
{
    public static PdfLayout Read(byte[] pdf, TemplateSettings settings)
    {
        using var document = PdfDocument.Open(pdf);
        var pageFilter = PageRange.Parse(settings.Pages, document.NumberOfPages);

        var pages = new List<PageLayout>();
        foreach (var page in document.GetPages())
        {
            if (!pageFilter.Contains(page.Number))
                continue;

            var words = settings.WordExtractor.Equals("nearestNeighbour", StringComparison.OrdinalIgnoreCase)
                ? page.GetWords(NearestNeighbourWordExtractor.Instance)
                : page.GetWords();

            pages.Add(new PageLayout
            {
                Number = page.Number,
                Width = page.Width,
                Height = page.Height,
                Lines = BuildLines(page.Number, words, settings)
            });
        }

        return new PdfLayout
        {
            PageCount = document.NumberOfPages,
            Pages = pages,
            Metadata = ReadMetadata(document),
            FormFields = ReadFormFields(document)
        };
    }

    /// <summary>Agrupa palavras em linhas pela baseline (tolerância LineTolerance) e ordena da esquerda para a direita.</summary>
    public static List<TextLine> BuildLines(int pageNumber, IEnumerable<Word> words, TemplateSettings settings)
    {
        var items = words
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .Select(w => (Word: w, Baseline: w.Letters.Count > 0 ? w.Letters[0].StartBaseLine.Y : w.BoundingBox.Bottom))
            .OrderByDescending(x => x.Baseline)
            .ThenBy(x => x.Word.BoundingBox.Left)
            .ToList();

        var groups = new List<List<Word>>();
        double? currentBaseline = null;
        foreach (var (word, baseline) in items)
        {
            if (currentBaseline is null || Math.Abs(currentBaseline.Value - baseline) > settings.LineTolerance)
            {
                groups.Add(new List<Word>());
                currentBaseline = baseline;
            }
            groups[^1].Add(word);
        }

        var lines = new List<TextLine>();
        foreach (var group in groups)
        {
            var boxes = group
                .OrderBy(w => w.BoundingBox.Left)
                .Select(w => new WordBox
                {
                    Text = w.Text.Trim(),
                    Left = w.BoundingBox.Left,
                    Right = w.BoundingBox.Right,
                    Bottom = w.BoundingBox.Bottom,
                    Top = w.BoundingBox.Top
                })
                .ToList();

            lines.Add(CreateLine(pageNumber, lines.Count, boxes, settings));
        }
        return lines;
    }

    public static TextLine CreateLine(int pageNumber, int index, List<WordBox> words, TemplateSettings settings)
    {
        var sb = new StringBuilder();
        foreach (var word in words)
        {
            if (sb.Length > 0) sb.Append(' ');
            word.Start = sb.Length;
            sb.Append(word.Text);
        }

        var cells = new List<TextCell>();
        WordBox? previous = null;
        foreach (var word in words)
        {
            if (previous is null || word.Left - previous.Right > settings.CellGap)
                cells.Add(new TextCell { Words = new List<WordBox>() });
            cells[^1].Words.Add(word);
            previous = word;
        }

        var text = sb.ToString();
        return new TextLine
        {
            Page = pageNumber,
            Index = index,
            Words = words,
            Cells = cells,
            Text = text,
            NormalizedText = TextNormalizer.Normalize(text, settings.IgnoreAccents)
        };
    }

    public static Dictionary<string, string?> ReadMetadata(PdfDocument document)
    {
        var info = document.Information;
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = info.Title,
            ["Author"] = info.Author,
            ["Subject"] = info.Subject,
            ["Keywords"] = info.Keywords,
            ["Creator"] = info.Creator,
            ["Producer"] = info.Producer,
            ["CreationDate"] = info.CreationDate,
            ["ModifiedDate"] = info.ModifiedDate,
            ["PageCount"] = document.NumberOfPages.ToString(),
            ["Version"] = document.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Lê campos de formulário (AcroForm), quando o PDF for um formulário preenchível.</summary>
    public static Dictionary<string, string?> ReadFormFields(PdfDocument document)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!document.TryGetForm(out var form))
            return result;

        foreach (var field in form.Fields)
        {
            var name = field.Information.PartialName ?? field.Information.MappingName ?? field.Information.AlternateName;
            if (string.IsNullOrEmpty(name))
                continue;

            result[name] = field switch
            {
                AcroTextField text => text.Value,
                AcroCheckboxField checkbox => checkbox.IsChecked ? "true" : "false",
                AcroComboBoxField combo => string.Join(", ", combo.SelectedOptions ?? Array.Empty<string>()),
                AcroListBoxField list => string.Join(", ", list.SelectedOptions ?? Array.Empty<string>()),
                _ => null
            };
        }
        return result;
    }
}

/// <summary>Interpreta intervalos de páginas como "1-3,5".</summary>
public sealed class PageRange
{
    private readonly HashSet<int>? _pages;

    private PageRange(HashSet<int>? pages) => _pages = pages;

    public bool Contains(int page) => _pages is null || _pages.Contains(page);

    public static PageRange Parse(string? spec, int pageCount)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return new PageRange(null);

        var pages = new HashSet<int>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = part.Split('-', StringSplitOptions.TrimEntries);
            var start = ResolvePage(bounds[0], pageCount);
            var end = bounds.Length > 1 ? ResolvePage(bounds[1], pageCount) : start;
            for (var p = start; p <= end; p++)
                pages.Add(p);
        }
        return new PageRange(pages);
    }

    // "last" pode ser usado para a última página (ex.: "2-last")
    private static int ResolvePage(string value, int pageCount) =>
        value.Equals("last", StringComparison.OrdinalIgnoreCase) ? pageCount
        : int.TryParse(value, out var n) && n > 0 ? n
        : throw new FormatException($"Intervalo de páginas inválido: '{value}'. Use, por exemplo, \"1\", \"1-3\", \"1,3,5-last\".");
}
