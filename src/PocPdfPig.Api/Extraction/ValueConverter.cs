using System.Globalization;
using System.Text.Json.Nodes;
using PocPdfPig.Api.Models;

namespace PocPdfPig.Api.Extraction;

/// <summary>Converte o texto extraído para o tipo pedido no template (dataType).</summary>
public static class ValueConverter
{
    public static readonly string[] SupportedTypes = ["string", "decimal", "integer", "date", "datetime", "boolean"];

    private static readonly string[] DateFormats =
    [
        "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "dd.MM.yyyy", "yyyy-MM-dd",
        "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "d/M/yyyy HH:mm:ss",
        "MM/yyyy", "M/yyyy", "MMMM 'de' yyyy", "MMMM / yyyy", "MMMM/yyyy", "MMM/yyyy", "MMM yyyy"
    ];

    private static readonly HashSet<string> TrueValues = new(StringComparer.OrdinalIgnoreCase)
        { "sim", "s", "true", "yes", "y", "x", "(x)", "1", "verdadeiro" };

    private static readonly HashSet<string> FalseValues = new(StringComparer.OrdinalIgnoreCase)
        { "nao", "não", "n", "false", "no", "0", "falso", "( )", "()" };

    public static bool TryConvert(string value, ValueOptions options, CultureInfo culture, out JsonNode? node, out string? error)
    {
        node = null;
        error = null;
        var type = (options.DataType ?? "string").Trim().ToLowerInvariant();

        switch (type)
        {
            case "string":
                node = JsonValue.Create(value);
                return true;

            case "decimal":
            {
                var numberCulture = options.Format is { Length: > 0 } ? CultureInfo.GetCultureInfo(options.Format) : culture;
                var clean = value.Replace("R$", "").Replace("%", "").Replace(" ", "");
                var negative = clean.StartsWith('(') && clean.EndsWith(')');
                clean = clean.Trim('(', ')');
                if (decimal.TryParse(clean, NumberStyles.Number, numberCulture, out var d))
                {
                    node = JsonValue.Create(negative ? -d : d);
                    return true;
                }
                error = $"não foi possível converter '{value}' para decimal ({numberCulture.Name})";
                return false;
            }

            case "integer":
            {
                var clean = value.Replace(" ", "");
                if (long.TryParse(clean, NumberStyles.Integer | NumberStyles.AllowThousands, culture, out var l))
                {
                    node = JsonValue.Create(l);
                    return true;
                }
                error = $"não foi possível converter '{value}' para inteiro";
                return false;
            }

            case "date":
            case "datetime":
            {
                var formats = options.Format is { Length: > 0 }
                    ? options.Format.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    : DateFormats;

                var dateCulture = WithoutDotsInMonthAbbreviations(culture);
                foreach (var format in formats)
                {
                    if (!DateTime.TryParseExact(value, format, dateCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
                        continue;

                    // ignora literais entre aspas (ex.: 'de') ao verificar se o formato tem dia
                    var hasDay = System.Text.RegularExpressions.Regex.Replace(format, "'[^']*'", "").Contains('d');
                    var output = type == "datetime" ? dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
                        : hasDay ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : dt.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                    node = JsonValue.Create(output);
                    return true;
                }
                error = $"não foi possível converter '{value}' para data (formatos: {string.Join(" | ", formats)})";
                return false;
            }

            case "boolean":
            {
                var clean = value.Trim();
                if (TrueValues.Contains(clean)) { node = JsonValue.Create(true); return true; }
                if (FalseValues.Contains(clean)) { node = JsonValue.Create(false); return true; }
                error = $"não foi possível converter '{value}' para boolean (use Sim/Não, S/N, X, true/false)";
                return false;
            }

            default:
                error = $"dataType '{options.DataType}' não suportado. Use: {string.Join(", ", SupportedTypes)}";
                return false;
        }
    }

    /// <summary>Em pt-BR o .NET abrevia meses como "jan."; nos PDFs aparece "JAN". Remove o ponto para o parse funcionar.</summary>
    private static CultureInfo WithoutDotsInMonthAbbreviations(CultureInfo culture) =>
        DateCultures.GetOrAdd(culture.Name, _ =>
        {
            var clone = (CultureInfo)culture.Clone();
            clone.DateTimeFormat.AbbreviatedMonthNames = clone.DateTimeFormat.AbbreviatedMonthNames.Select(m => m.TrimEnd('.')).ToArray();
            clone.DateTimeFormat.AbbreviatedMonthGenitiveNames = clone.DateTimeFormat.AbbreviatedMonthGenitiveNames.Select(m => m.TrimEnd('.')).ToArray();
            return CultureInfo.ReadOnly(clone);
        });

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CultureInfo> DateCultures = new();

    public static string ApplyTransforms(string value, IEnumerable<string>? transforms)
    {
        if (transforms is null) return value;

        foreach (var t in transforms)
        {
            value = t.Trim().ToLowerInvariant() switch
            {
                "trim" => value.Trim(),
                "upper" => value.ToUpperInvariant(),
                "lower" => value.ToLowerInvariant(),
                "digitsonly" => new string(value.Where(char.IsDigit).ToArray()),
                "removespaces" => value.Replace(" ", ""),
                "collapsespaces" => TextNormalizer.CollapseSpaces(value),
                _ => throw new InvalidOperationException($"Transformação '{t}' desconhecida.")
            };
        }
        return value;
    }

    public static readonly string[] SupportedTransforms = ["trim", "upper", "lower", "digitsOnly", "removeSpaces", "collapseSpaces"];
}
