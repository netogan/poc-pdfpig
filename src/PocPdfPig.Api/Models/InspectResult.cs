namespace PocPdfPig.Api.Models;

/// <summary>Raio-X do PDF: tudo o que o motor "enxerga". Use para montar templates.</summary>
public sealed class InspectResult
{
    public string? FileName { get; set; }
    public int PageCount { get; set; }
    public Dictionary<string, string?> Metadata { get; set; } = new();
    public Dictionary<string, string?> FormFields { get; set; } = new();
    public List<InspectPage> Pages { get; set; } = new();
}

public sealed class InspectPage
{
    public int Number { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int LetterCount { get; set; }
    public int ImageCount { get; set; }

    /// <summary>
    /// Texto da página exatamente como a estratégia "regex" enxerga: uma linha por linha visual,
    /// palavras separadas por um espaço.
    /// </summary>
    public string Text { get; set; } = "";

    public List<InspectLine> Lines { get; set; } = new();

    /// <summary>Blocos de texto detectados pelo algoritmo Docstrum do PdfPig (includeBlocks=true).</summary>
    public List<InspectBlock>? Blocks { get; set; }

    /// <summary>Texto na ordem em que foi escrito no arquivo (ContentOrderTextExtractor do PdfPig, includeContentOrderText=true).</summary>
    public string? ContentOrderText { get; set; }
}

public sealed class InspectLine
{
    public int Index { get; set; }
    /// <summary>Coordenada vertical da base da linha (0 = rodapé da página; quanto maior, mais alto).</summary>
    public double Y { get; set; }
    public double Left { get; set; }
    public double Right { get; set; }
    public string Text { get; set; } = "";
    /// <summary>Células: grupos de palavras separados por um espaço maior que cellGap.</summary>
    public List<InspectBox> Cells { get; set; } = new();
    /// <summary>Palavras com coordenadas (includeWords=true).</summary>
    public List<InspectBox>? Words { get; set; }
}

public sealed class InspectBox
{
    public string Text { get; set; } = "";
    public double Left { get; set; }
    public double Bottom { get; set; }
    public double Right { get; set; }
    public double Top { get; set; }
}

public sealed class InspectBlock
{
    public string Text { get; set; } = "";
    public double Left { get; set; }
    public double Bottom { get; set; }
    public double Right { get; set; }
    public double Top { get; set; }
}
