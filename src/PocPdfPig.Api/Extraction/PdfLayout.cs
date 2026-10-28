namespace PocPdfPig.Api.Extraction;

/// <summary>
/// Representação "amigável" do PDF montada a partir das palavras que o PdfPig devolve:
/// Documento → Páginas → Linhas → Células → Palavras (todas com coordenadas).
/// </summary>
public sealed class PdfLayout
{
    public required int PageCount { get; init; }
    public required List<PageLayout> Pages { get; init; }
    public required Dictionary<string, string?> Metadata { get; init; }
    public required Dictionary<string, string?> FormFields { get; init; }

    /// <summary>Todas as linhas do documento, em ordem de leitura (página a página, de cima para baixo).</summary>
    public IEnumerable<TextLine> AllLines => Pages.SelectMany(p => p.Lines);
}

public sealed class PageLayout
{
    public required int Number { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required List<TextLine> Lines { get; init; }
}

/// <summary>Palavra com sua caixa delimitadora (coordenadas PDF: origem no canto inferior esquerdo).</summary>
public sealed class WordBox
{
    public required string Text { get; init; }
    public required double Left { get; init; }
    public required double Right { get; init; }
    public required double Bottom { get; init; }
    public required double Top { get; init; }

    /// <summary>Posição (em caracteres) da palavra dentro de <see cref="TextLine.Text"/>.</summary>
    public int Start { get; set; }
    public int End => Start + Text.Length;
    public double CenterX => (Left + Right) / 2;
}

/// <summary>Grupo de palavras próximas na mesma linha (separadas por espaço maior que CellGap).</summary>
public sealed class TextCell
{
    public required List<WordBox> Words { get; init; }
    public string Text => string.Join(' ', Words.Select(w => w.Text));
    public double Left => Words[0].Left;
    public double Right => Words.Max(w => w.Right);
}

public sealed class TextLine
{
    public required int Page { get; init; }

    /// <summary>Índice da linha dentro da página (0 = mais alta).</summary>
    public required int Index { get; init; }
    public required List<WordBox> Words { get; init; }
    public required List<TextCell> Cells { get; init; }

    /// <summary>Texto da linha: palavras separadas por um espaço.</summary>
    public required string Text { get; init; }

    /// <summary>Mesmo texto normalizado (sem acentos, se configurado). Mesmo tamanho de <see cref="Text"/>.</summary>
    public required string NormalizedText { get; init; }

    public double Bottom => Words.Min(w => w.Bottom);
    public double Top => Words.Max(w => w.Top);
    public double Left => Words[0].Left;
    public double Right => Words.Max(w => w.Right);
    public double Height => Math.Max(1, Top - Bottom);

    public override string ToString() => $"[p{Page} y={Bottom:F0}] {Text}";
}
