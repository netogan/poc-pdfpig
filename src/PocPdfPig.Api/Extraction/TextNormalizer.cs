using System.Globalization;
using System.Text;

namespace PocPdfPig.Api.Extraction;

/// <summary>
/// Normalização de texto que PRESERVA o tamanho da string (1 caractere → 1 caractere),
/// permitindo comparar no texto normalizado e recortar o valor do texto original pelos mesmos índices.
/// </summary>
public static class TextNormalizer
{
    public static string Normalize(string text, bool ignoreAccents)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(NormalizeChar(c, ignoreAccents));
        return sb.ToString();
    }

    private static char NormalizeChar(char c, bool ignoreAccents)
    {
        switch (c)
        {
            case ' ': return ' ';                  // espaço não separável
            case '–' or '—': return '-';      // travessões
            case '‘' or '’': return '\'';
            case '“' or '”': return '"';
        }

        if (!ignoreAccents || c < 128)
            return c;

        var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
        return decomposed.Length > 0 && CharUnicodeInfo.GetUnicodeCategory(decomposed[0]) != UnicodeCategory.NonSpacingMark
            ? decomposed[0]
            : c;
    }

    /// <summary>Colapsa espaços múltiplos e remove espaços nas pontas.</summary>
    public static string CollapseSpaces(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
