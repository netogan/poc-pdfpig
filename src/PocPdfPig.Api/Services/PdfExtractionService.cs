using System.Diagnostics;
using PocPdfPig.Api.Extraction;
using PocPdfPig.Api.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace PocPdfPig.Api.Services;

public sealed class InspectOptions
{
    public string? Pages { get; set; }
    public double LineTolerance { get; set; } = 3;
    public double CellGap { get; set; } = 8;
    public bool IncludeWords { get; set; }
    public bool IncludeBlocks { get; set; }
    public bool IncludeContentOrderText { get; set; }
    public string WordExtractor { get; set; } = "default";
}

/// <summary>Fachada usada pelos controllers: extração por template e inspeção do PDF.</summary>
public sealed class PdfExtractionService
{
    public ExtractionResult Extract(byte[] pdf, ExtractionTemplate template, string? fileName, bool debug)
    {
        var sw = Stopwatch.StartNew();
        var layout = PdfLayoutReader.Read(pdf, template.Settings);
        var result = new ExtractionEngine(template, layout, debug).Run();
        result.FileName = fileName;
        result.ElapsedMs = sw.ElapsedMilliseconds;
        return result;
    }

    public InspectResult Inspect(byte[] pdf, string? fileName, InspectOptions options)
    {
        var settings = new TemplateSettings
        {
            LineTolerance = options.LineTolerance,
            CellGap = options.CellGap,
            WordExtractor = options.WordExtractor
        };

        using var document = PdfDocument.Open(pdf);
        var range = PageRange.Parse(options.Pages, document.NumberOfPages);

        var result = new InspectResult
        {
            FileName = fileName,
            PageCount = document.NumberOfPages,
            Metadata = PdfLayoutReader.ReadMetadata(document),
            FormFields = PdfLayoutReader.ReadFormFields(document)
        };

        foreach (var page in document.GetPages())
        {
            if (!range.Contains(page.Number)) continue;

            var words = settings.WordExtractor.Equals("nearestNeighbour", StringComparison.OrdinalIgnoreCase)
                ? page.GetWords(NearestNeighbourWordExtractor.Instance).ToList()
                : page.GetWords().ToList();
            var lines = PdfLayoutReader.BuildLines(page.Number, words, settings);

            var inspectPage = new InspectPage
            {
                Number = page.Number,
                Width = Math.Round(page.Width, 2),
                Height = Math.Round(page.Height, 2),
                LetterCount = page.Letters.Count,
                ImageCount = page.GetImages().Count(),
                Text = string.Join('\n', lines.Select(l => l.Text)),
                Lines = lines.Select(l => new InspectLine
                {
                    Index = l.Index,
                    Y = R(l.Bottom),
                    Left = R(l.Left),
                    Right = R(l.Right),
                    Text = l.Text,
                    Cells = l.Cells.Select(c => new InspectBox
                    {
                        Text = c.Text, Left = R(c.Left), Right = R(c.Right),
                        Bottom = R(c.Words.Min(w => w.Bottom)), Top = R(c.Words.Max(w => w.Top))
                    }).ToList(),
                    Words = options.IncludeWords
                        ? l.Words.Select(w => new InspectBox { Text = w.Text, Left = R(w.Left), Bottom = R(w.Bottom), Right = R(w.Right), Top = R(w.Top) }).ToList()
                        : null
                }).ToList()
            };

            if (options.IncludeBlocks)
            {
                inspectPage.Blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words)
                    .Select(b => new InspectBlock
                    {
                        Text = b.Text,
                        Left = R(b.BoundingBox.Left), Bottom = R(b.BoundingBox.Bottom),
                        Right = R(b.BoundingBox.Right), Top = R(b.BoundingBox.Top)
                    }).ToList();
            }

            if (options.IncludeContentOrderText)
                inspectPage.ContentOrderText = ContentOrderTextExtractor.GetText(page, true);

            result.Pages.Add(inspectPage);
        }
        return result;
    }

    private static double R(double v) => Math.Round(v, 1);
}
