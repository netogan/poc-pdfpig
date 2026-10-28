using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PocPdfPig.Api.Extraction;
using PocPdfPig.Api.Models;

namespace PocPdfPig.Api.Services;

/// <summary>Lê o JSON do template e valida as regras de cada tipo de campo, devolvendo mensagens amigáveis.</summary>
public static class TemplateParser
{
    public static readonly string[] FieldTypes =
        ["labelRight", "labelBelow", "regex", "region", "table", "group", "metadata", "formField"];

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // propriedade desconhecida (ex.: erro de digitação "lable") gera erro em vez de ser ignorada
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static (ExtractionTemplate? Template, List<string> Errors) Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (null, ["O template (JSON) não foi informado."]);

        ExtractionTemplate? template;
        try
        {
            template = JsonSerializer.Deserialize<ExtractionTemplate>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return (null, [$"JSON inválido: {ex.Message}"]);
        }

        if (template is null)
            return (null, ["Template vazio."]);

        var errors = Validate(template);
        return (errors.Count == 0 ? template : null, errors);
    }

    public static List<string> Validate(ExtractionTemplate template)
    {
        var errors = new List<string>();
        var s = template.Settings ??= new TemplateSettings();

        if (template.Fields is not { Count: > 0 })
            errors.Add("O template precisa ter ao menos um item em \"fields\".");

        if (s.WordExtractor is not ("default" or "nearestNeighbour"))
            errors.Add("settings.wordExtractor deve ser \"default\" ou \"nearestNeighbour\".");
        if (s.LineTolerance <= 0) errors.Add("settings.lineTolerance deve ser maior que zero.");
        if (s.CellGap <= 0) errors.Add("settings.cellGap deve ser maior que zero.");
        try { CultureInfo.GetCultureInfo(s.Culture); }
        catch (CultureNotFoundException) { errors.Add($"settings.culture '{s.Culture}' inválida."); }
        try { PageRange.Parse(s.Pages, pageCount: 1000); }
        catch (FormatException ex) { errors.Add($"settings.pages: {ex.Message}"); }
        CheckRegexList(s.IgnoreLines, "settings.ignoreLines", errors);

        ValidateFields(template.Fields ?? new(), "fields", errors);
        return errors;
    }

    private static void ValidateFields(List<FieldDefinition> fields, string prefix, List<string> errors)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            var path = string.IsNullOrWhiteSpace(f.Name) ? $"{prefix}[{i}]" : $"{prefix}.{f.Name}";

            if (string.IsNullOrWhiteSpace(f.Name))
                errors.Add($"{path}: \"name\" é obrigatório.");
            else if (!names.Add(f.Name))
                errors.Add($"{path}: nome duplicado no mesmo nível.");

            var type = FieldTypes.FirstOrDefault(t => t.Equals(f.Type, StringComparison.OrdinalIgnoreCase));
            if (type is null)
            {
                errors.Add($"{path}: \"type\" deve ser um de: {string.Join(", ", FieldTypes)}.");
                continue;
            }

            ValidateValueOptions(f, path, errors);
            CheckRegex(f.LabelPattern, $"{path}.labelPattern", errors);

            switch (type)
            {
                case "labelRight":
                case "labelBelow":
                    RequireLabel(f, path, errors);
                    if (f.Take is not ("cell" or "line"))
                        errors.Add($"{path}: \"take\" deve ser \"cell\" ou \"line\".");
                    break;

                case "regex":
                    if (string.IsNullOrEmpty(f.Pattern)) errors.Add($"{path}: \"pattern\" é obrigatório para type=regex.");
                    CheckRegex(f.Pattern, $"{path}.pattern", errors);
                    ValidateColumns(f.Columns, path, errors, requirePosition: false);
                    break;

                case "region":
                    if (f.Region is null) errors.Add($"{path}: \"region\" é obrigatório para type=region.");
                    else if (f.Region.Right <= f.Region.Left || f.Region.Top <= f.Region.Bottom)
                        errors.Add($"{path}: region precisa de right > left e top > bottom.");
                    break;

                case "table":
                    if (string.IsNullOrEmpty(f.Header) && string.IsNullOrEmpty(f.HeaderPattern))
                        errors.Add($"{path}: informe \"header\" ou \"headerPattern\" para type=table.");
                    CheckRegex(f.HeaderPattern, $"{path}.headerPattern", errors);
                    CheckRegex(f.RowPattern, $"{path}.rowPattern", errors);
                    CheckRegex(f.End, $"{path}.end", errors);
                    ValidateColumns(f.Columns, path, errors, requirePosition: true);
                    break;

                case "group":
                    if (f.Fields is not { Count: > 0 })
                        errors.Add($"{path}: type=group precisa de \"fields\".");
                    if (f.PerPage && f.Start is not null)
                        errors.Add($"{path}: use \"perPage\" OU \"start\", não os dois.");
                    CheckRegex(f.Start, $"{path}.start", errors);
                    CheckRegex(f.End, $"{path}.end", errors);
                    CheckRegexList(f.IgnoreLines, $"{path}.ignoreLines", errors);
                    if (f.Fields is not null) ValidateFields(f.Fields, path, errors);
                    break;

                case "metadata":
                case "formField":
                    if (string.IsNullOrEmpty(f.Key)) errors.Add($"{path}: \"key\" é obrigatório para type={type}.");
                    break;
            }
        }
    }

    private static void ValidateColumns(List<ColumnDefinition>? columns, string path, List<string> errors, bool requirePosition)
    {
        if (columns is null) return;
        foreach (var c in columns)
        {
            var cpath = $"{path}.columns.{(string.IsNullOrEmpty(c.Name) ? "?" : c.Name)}";
            if (string.IsNullOrWhiteSpace(c.Name)) errors.Add($"{cpath}: \"name\" é obrigatório.");
            if (requirePosition && c.Name != "*" && c.Header is null && c.HeaderPattern is null && (c.Left is null || c.Right is null))
                errors.Add($"{cpath}: informe \"header\", \"headerPattern\" ou \"left\"+\"right\".");
            CheckRegex(c.HeaderPattern, $"{cpath}.headerPattern", errors);
            ValidateValueOptions(c, cpath, errors);
        }
    }

    private static void ValidateValueOptions(ValueOptions o, string path, List<string> errors)
    {
        if (!ValueConverter.SupportedTypes.Contains(o.DataType, StringComparer.OrdinalIgnoreCase))
            errors.Add($"{path}: dataType '{o.DataType}' inválido. Use: {string.Join(", ", ValueConverter.SupportedTypes)}.");
        CheckRegex(o.ValuePattern, $"{path}.valuePattern", errors);
        foreach (var t in o.Transforms ?? new())
            if (!ValueConverter.SupportedTransforms.Contains(t, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{path}: transform '{t}' inválido. Use: {string.Join(", ", ValueConverter.SupportedTransforms)}.");
    }

    private static void RequireLabel(FieldDefinition f, string path, List<string> errors)
    {
        if (string.IsNullOrEmpty(f.Label) && string.IsNullOrEmpty(f.LabelPattern))
            errors.Add($"{path}: informe \"label\" ou \"labelPattern\" para type={f.Type}.");
        if (f.Occurrence == 0)
            errors.Add($"{path}: \"occurrence\" começa em 1 (use -1 para a última).");
    }

    private static void CheckRegexList(List<string>? patterns, string path, List<string> errors)
    {
        if (patterns is null) return;
        for (var i = 0; i < patterns.Count; i++)
            CheckRegex(patterns[i], $"{path}[{i}]", errors);
    }

    private static void CheckRegex(string? pattern, string path, List<string> errors)
    {
        if (pattern is null) return;
        try { _ = new Regex(pattern); }
        catch (ArgumentException ex) { errors.Add($"{path}: regex inválida ({ex.Message})."); }
    }
}
