using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PocPdfPig.Api.Models;

namespace PocPdfPig.Api.Extraction;

/// <summary>
/// Executa um <see cref="ExtractionTemplate"/> sobre um <see cref="PdfLayout"/>.
/// Cada "type" de campo é uma estratégia de localização:
/// labelRight, labelBelow, regex, region, table, group, metadata, formField.
/// </summary>
public sealed class ExtractionEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private readonly ExtractionTemplate _template;
    private readonly TemplateSettings _settings;
    private readonly PdfLayout _layout;
    private readonly CultureInfo _culture;
    private readonly bool _debug;
    private readonly Dictionary<(string, bool), Regex> _regexCache = new();

    private readonly List<string> _warnings = new();
    private readonly List<TraceEntry> _trace = new();
    private bool _missingRequired;

    public ExtractionEngine(ExtractionTemplate template, PdfLayout layout, bool debug)
    {
        _template = template;
        _settings = template.Settings;
        _layout = layout;
        _debug = debug;
        _culture = CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(_settings.Culture) ? "pt-BR" : _settings.Culture);
    }

    public ExtractionResult Run()
    {
        var lines = FilterLines(_layout.AllLines, _settings.IgnoreLines).ToList();
        var data = EvaluateFields(_template.Fields, lines, prefix: "");

        return new ExtractionResult
        {
            Template = _template.Name,
            PageCount = _layout.PageCount,
            Success = !_missingRequired,
            Data = data,
            Warnings = _warnings,
            Trace = _debug ? _trace : null
        };
    }

    // =====================================================================
    // Despacho por tipo
    // =====================================================================

    private JsonObject EvaluateFields(IEnumerable<FieldDefinition> fields, IReadOnlyList<TextLine> scope, string prefix)
    {
        var obj = new JsonObject();
        foreach (var field in fields)
        {
            var path = prefix.Length == 0 ? field.Name : $"{prefix}.{field.Name}";
            JsonNode? value;
            try
            {
                value = Evaluate(field, scope, path);
            }
            catch (RegexMatchTimeoutException)
            {
                _warnings.Add($"{path}: regex demorou demais (timeout). Simplifique a expressão.");
                value = null;
            }

            if (field.Required && IsEmpty(value))
            {
                _missingRequired = true;
                _warnings.Add($"{path}: campo obrigatório não encontrado.");
            }
            obj[field.Name] = value;
        }
        return obj;
    }

    private JsonNode? Evaluate(FieldDefinition field, IReadOnlyList<TextLine> scope, string path) =>
        field.Type.Trim().ToLowerInvariant() switch
        {
            "labelright" => LabelRight(field, scope, path),
            "labelbelow" => LabelBelow(field, scope, path),
            "regex" => RegexField(field, scope, path),
            "region" => RegionField(field, scope, path),
            "table" => Table(field, scope, path),
            "group" => Group(field, scope, path),
            "metadata" => FromDictionary(field, _layout.Metadata, path),
            "formfield" => FromDictionary(field, _layout.FormFields, path),
            _ => throw new InvalidOperationException($"{path}: type '{field.Type}' desconhecido.")
        };

    // =====================================================================
    // labelRight: valor à direita do rótulo, na mesma linha
    // =====================================================================

    private JsonNode? LabelRight(FieldDefinition field, IReadOnlyList<TextLine> scope, string path)
    {
        var match = FindLabel(field, scope);
        if (match is null)
            return NotFound(field, path, "rótulo não encontrado");

        var line = match.Line;
        var parts = new List<(string Text, WordBox Word)>();
        foreach (var word in line.Words)
        {
            if (word.End <= match.End)
                continue;                                   // palavra do rótulo (ou antes dele)

            if (word.Start < match.End)
            {
                // rótulo termina no meio da palavra (ex.: rótulo "Registro" e palavra "Registro:")
                var rest = word.Text[(match.End - word.Start)..].TrimStart(':');
                if (rest.Length > 0) parts.Add((rest, word));
                continue;
            }
            parts.Add((word.Text, word));
        }

        if (field.Take.Equals("cell", StringComparison.OrdinalIgnoreCase))
        {
            // pega apenas o primeiro "bloco" de palavras após o rótulo (até um espaço maior que CellGap)
            var cell = new List<(string Text, WordBox Word)>();
            foreach (var part in parts)
            {
                if (cell.Count > 0 && part.Word.Left - cell[^1].Word.Right > _settings.CellGap)
                    break;
                cell.Add(part);
            }
            parts = cell;
        }

        if (field.MaxWords is > 0)
            parts = parts.Take(field.MaxWords.Value).ToList();

        var raw = string.Join(' ', parts.Select(p => p.Text)).TrimStart(':', ' ');

        if (field.Multiline && parts.Count > 0)
            raw = AppendContinuationLines(raw, line, parts[0].Word.Left, scope, column: null);

        return Finish(field, raw, path, line);
    }

    /// <summary>Concatena as linhas seguintes que parecem continuação do valor (mesma página, logo abaixo, alinhadas à direita do início do valor).</summary>
    private string AppendContinuationLines(string raw, TextLine line, double valueLeft, IReadOnlyList<TextLine> scope, Func<TextLine, string?>? column)
    {
        var index = IndexOf(scope, line);
        var previous = line;
        for (var i = index + 1; i < scope.Count; i++)
        {
            var next = scope[i];
            if (next.Page != line.Page || previous.Bottom - next.Top > next.Height * 1.5)
                break;

            string? text;
            if (column is not null)
                text = column(next);
            else
                text = next.Words.All(w => w.Left >= valueLeft - 3) ? next.Text : null;

            if (string.IsNullOrWhiteSpace(text))
                break;
            raw += " " + text;
            previous = next;
        }
        return raw;
    }

    // =====================================================================
    // labelBelow: valor na(s) linha(s) abaixo do rótulo, na mesma "coluna"
    // =====================================================================

    private JsonNode? LabelBelow(FieldDefinition field, IReadOnlyList<TextLine> scope, string path)
    {
        var match = FindLabel(field, scope);
        if (match is null)
            return NotFound(field, path, "rótulo não encontrado");

        var header = match.Line;
        var spans = header.Cells.Select(c => (c.Left, c.Right)).ToList();
        var target = header.Cells.FindIndex(c => c.Words.Any(w => w.End > match.Start && w.Start < match.End));

        string? ColumnText(TextLine l)
        {
            var words = l.Words.Where(w => ColumnAssigner.Assign(w, spans) == target).Select(w => w.Text).ToList();
            return words.Count == 0 ? null : string.Join(' ', words);
        }

        var index = IndexOf(scope, header);
        var collected = new List<string>();
        TextLine? first = null;
        var wanted = Math.Max(1, field.Lines);
        var skipped = 0;
        for (var i = index + 1; i < scope.Count && collected.Count < wanted; i++)
        {
            var line = scope[i];
            if (line.Page != header.Page) break;

            var text = ColumnText(line);
            if (text is null)
            {
                if (collected.Count > 0 || ++skipped > 3) break;   // até 3 linhas vazias antes do valor
                continue;
            }
            first ??= line;
            collected.Add(text);
        }

        if (first is null)
            return NotFound(field, path, $"nada encontrado abaixo do rótulo (linha: {header})");

        var raw = string.Join(' ', collected);
        if (field.Multiline)
        {
            var last = scope[IndexOf(scope, first) + collected.Count - 1];
            raw = AppendContinuationLines(raw, last, 0, scope, ColumnText);
        }
        return Finish(field, raw, path, first);
    }

    // =====================================================================
    // regex: expressão regular sobre o texto do escopo
    // =====================================================================

    private JsonNode? RegexField(FieldDefinition field, IReadOnlyList<TextLine> scope, string path)
    {
        var lines = field.Page is { } p ? scope.Where(l => l.Page == p).ToList() : scope;
        var normalized = string.Join('\n', lines.Select(l => l.NormalizedText));
        var original = string.Join('\n', lines.Select(l => l.Text));
        var regex = GetRegex(field.Pattern!, multiline: true);

        var matches = regex.Matches(normalized).Where(m => m.Success).ToList();
        if (matches.Count == 0)
            return field.All ? new JsonArray() : NotFound(field, path, "regex sem correspondência");

        if (field.All)
        {
            var array = new JsonArray();
            for (var i = 0; i < matches.Count; i++)
                array.Add(RegexMatchToNode(field, regex, matches[i], original, $"{path}[{i}]"));
            return array;
        }

        var chosen = PickOccurrence(matches, field.Occurrence);
        return chosen is null ? NotFound(field, path, $"ocorrência {field.Occurrence} não existe") : RegexMatchToNode(field, regex, chosen, original, path);
    }

    private JsonNode? RegexMatchToNode(FieldDefinition field, Regex regex, Match match, string original, string path)
    {
        string Slice(Group g) => original.Substring(g.Index, g.Length);

        if (!string.IsNullOrEmpty(field.Group))
        {
            var g = int.TryParse(field.Group, out var n) ? match.Groups[n] : match.Groups[field.Group];
            return Finish(field, g.Success ? Slice(g) : null, path, null);
        }

        var named = regex.GetGroupNames().Where(n => !int.TryParse(n, out _)).ToList();
        if (named.Count > 1 && !named.Contains("value"))
        {
            // vários grupos nomeados → objeto { grupo: valor }, com conversão definida em "columns" (opcional)
            var obj = new JsonObject();
            foreach (var name in named)
            {
                var g = match.Groups[name];
                var options = (ValueOptions?)field.Columns?.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? new ValueOptions();
                obj[name] = ProcessValue(g.Success ? Slice(g) : null, options, $"{path}.{name}");
            }
            AddTrace(path, field.Type, null, match.Value);
            return obj;
        }

        var group = named.Count == 1 ? match.Groups[named[0]]
            : named.Contains("value") ? match.Groups["value"]
            : match.Groups.Count > 1 ? match.Groups[1]
            : match.Groups[0];
        return Finish(field, group.Success ? Slice(group) : null, path, null);
    }

    // =====================================================================
    // region: palavras dentro de um retângulo (absoluto ou relativo ao rótulo)
    // =====================================================================

    private JsonNode? RegionField(FieldDefinition field, IReadOnlyList<TextLine> scope, string path)
    {
        var r = field.Region!;
        int page;
        double left, bottom, right, top;

        if (field.Label is not null || field.LabelPattern is not null)
        {
            var match = FindLabel(field, scope);
            if (match is null)
                return NotFound(field, path, "rótulo âncora não encontrado");
            page = match.Line.Page;
            (left, bottom, right, top) = (match.Left + r.Left, match.Bottom + r.Bottom, match.Left + r.Right, match.Bottom + r.Top);
        }
        else
        {
            page = r.Page ?? field.Page ?? scope.FirstOrDefault()?.Page ?? 1;
            (left, bottom, right, top) = (r.Left, r.Bottom, r.Right, r.Top);
        }

        var texts = new List<string>();
        TextLine? firstLine = null;
        foreach (var line in scope.Where(l => l.Page == page))
        {
            var words = line.Words
                .Where(w => w.CenterX >= left && w.CenterX <= right && (w.Bottom + w.Top) / 2 >= bottom && (w.Bottom + w.Top) / 2 <= top)
                .Select(w => w.Text)
                .ToList();
            if (words.Count == 0) continue;
            firstLine ??= line;
            texts.Add(string.Join(' ', words));
        }

        return texts.Count == 0
            ? NotFound(field, path, $"nenhuma palavra na região p{page} [{left:F0},{bottom:F0} - {right:F0},{top:F0}]")
            : Finish(field, string.Join(' ', texts), path, firstLine);
    }

    // =====================================================================
    // table: cabeçalho + linhas de dados, colunas pela posição horizontal
    // =====================================================================

    private sealed record ColumnSpan(string Name, ValueOptions Options, double Left, double Right, bool Output);

    private JsonNode? Table(FieldDefinition field, IReadOnlyList<TextLine> scope, string path)
    {
        var rowRegex = field.RowPattern is null ? null : GetRegex(field.RowPattern, multiline: false);
        var endRegex = field.End is null ? null : GetRegex(field.End, multiline: false);
        var headerRegex = field.HeaderPattern is null ? null : GetRegex(field.HeaderPattern, multiline: false);

        var rows = new List<(TextLine Line, Dictionary<int, List<string>> Cells, List<ColumnSpan> Layout)>();
        List<ColumnSpan>? layout = null;
        var headerCount = 0;

        foreach (var line in scope)
        {
            if (field.Page is { } p && line.Page != p) continue;

            var isHeader = headerRegex?.IsMatch(line.NormalizedText) ?? FindInLine(line, field.Header!).Any();
            if (isHeader)
            {
                // a linha só vale como cabeçalho se tiver ao menos metade das colunas declaradas
                // (evita confundir com uma linha comum que contenha o mesmo texto)
                var (candidate, missing) = BuildTableLayout(field, line);
                var declaredByHeader = field.Columns?.Count(c => c.Name != "*" && (c.Left is null || c.Right is null)) ?? 0;
                if (declaredByHeader == 0 || missing.Count * 2 <= declaredByHeader)
                {
                    if (headerCount++ == 0)
                        _warnings.AddRange(missing.Select(m => $"{path}: coluna {m} não encontrada no cabeçalho: \"{line.Text}\""));
                    layout = candidate;
                    continue;
                }
            }
            if (layout is null) continue;
            if (endRegex?.IsMatch(line.NormalizedText) == true) break;

            if (rowRegex is null || rowRegex.IsMatch(line.NormalizedText))
            {
                rows.Add((line, AssignToColumns(line, layout), layout));
            }
            else if (field.MergeContinuationLines && rows.Count > 0 && rows[^1].Line.Page == line.Page)
            {
                foreach (var (col, words) in AssignToColumns(line, layout))
                {
                    if (!rows[^1].Cells.TryGetValue(col, out var list))
                        rows[^1].Cells[col] = list = new List<string>();
                    list.AddRange(words);
                }
            }
        }

        if (headerCount == 0)
            return NotFound(field, path, "cabeçalho da tabela não encontrado");

        AddTrace(path, field.Type, rows.FirstOrDefault().Line, $"{rows.Count} linha(s)");
        var array = new JsonArray();
        for (var r = 0; r < rows.Count; r++)
        {
            var (_, cells, cols) = rows[r];
            var obj = new JsonObject();
            for (var c = 0; c < cols.Count; c++)
            {
                if (!cols[c].Output) continue;
                var raw = cells.TryGetValue(c, out var words) ? string.Join(' ', words) : null;
                obj[cols[c].Name] = ProcessValue(raw, cols[c].Options, $"{path}[{r}].{cols[c].Name}");
            }
            array.Add(obj);
        }
        return array;
    }

    private (List<ColumnSpan> Layout, List<string> Missing) BuildTableLayout(FieldDefinition field, TextLine header)
    {
        var spans = new List<ColumnSpan>();
        var missing = new List<string>();
        // coluna "*" = opções (dataType etc.) aplicadas às colunas automáticas
        var allColumns = field.Columns ?? new List<ColumnDefinition>();
        var autoOptions = (ValueOptions?)allColumns.FirstOrDefault(c => c.Name == "*") ?? new ValueOptions();
        var declared = allColumns.Where(c => c.Name != "*").ToList();

        foreach (var col in declared)
        {
            if (col.Left is { } l && col.Right is { } r)
            {
                spans.Add(new ColumnSpan(col.Name, col, l, r, true));
                continue;
            }

            LabelMatch? m = col.HeaderPattern is not null
                ? FindPatternInLine(header, GetRegex(col.HeaderPattern, false)).FirstOrDefault()
                : FindInLine(header, col.Header ?? col.Name).FirstOrDefault();

            if (m is null)
            {
                missing.Add($"'{col.Name}' (cabeçalho '{col.Header ?? col.HeaderPattern ?? col.Name}')");
                spans.Add(new ColumnSpan(col.Name, col, double.NaN, double.NaN, true));
                continue;
            }
            spans.Add(new ColumnSpan(col.Name, col, m.Left, m.Right, true));
        }

        // células do cabeçalho que não pertencem a nenhuma coluna declarada também viram colunas
        // (assim as palavras abaixo delas não "vazam" para colunas vizinhas)
        var outputOthers = declared.Count == 0 || field.IncludeOtherColumns;
        foreach (var cell in header.Cells)
        {
            var taken = spans.Any(s => !double.IsNaN(s.Left) && Math.Min(s.Right, cell.Right) - Math.Max(s.Left, cell.Left) > 0);
            if (!taken)
                spans.Add(new ColumnSpan(cell.Text, autoOptions, cell.Left, cell.Right, outputOthers));
        }
        return (spans, missing);
    }

    private static Dictionary<int, List<string>> AssignToColumns(TextLine line, List<ColumnSpan> layout)
    {
        var valid = layout.Select((s, i) => (s, i)).Where(x => !double.IsNaN(x.s.Left)).ToList();
        var spans = valid.Select(x => (x.s.Left, x.s.Right)).ToList();
        var result = new Dictionary<int, List<string>>();
        foreach (var word in line.Words)
        {
            var idx = ColumnAssigner.Assign(word, spans);
            if (idx < 0) continue;
            var col = valid[idx].i;
            if (!result.TryGetValue(col, out var list))
                result[col] = list = new List<string>();
            list.Add(word.Text);
        }
        return result;
    }

    // =====================================================================
    // group: divide o escopo em blocos e extrai sub-campos de cada bloco
    // =====================================================================

    private JsonNode? Group(FieldDefinition field, IReadOnlyList<TextLine> scope, string path)
    {
        var lines = FilterLines(scope, field.IgnoreLines).ToList();
        if (field.Page is { } page) lines = lines.Where(l => l.Page == page).ToList();

        var blocks = new List<List<TextLine>>();
        if (field.PerPage)
        {
            blocks.AddRange(lines.GroupBy(l => l.Page).Select(g => g.ToList()));
        }
        else
        {
            var startRegex = field.Start is null ? null : GetRegex(field.Start, false);
            var endRegex = field.End is null ? null : GetRegex(field.End, false);
            List<TextLine>? current = startRegex is null ? new List<TextLine>() : null;
            if (current is not null) blocks.Add(current);

            foreach (var line in lines)
            {
                if (startRegex?.IsMatch(line.NormalizedText) == true)
                {
                    current = new List<TextLine>();
                    blocks.Add(current);
                }
                else if (current is not null && endRegex?.IsMatch(line.NormalizedText) == true)
                {
                    current = null;
                    if (startRegex is null) break;
                    continue;
                }
                current?.Add(line);
            }
        }

        blocks = blocks.Where(b => b.Count > 0).ToList();
        AddTrace(path, field.Type, blocks.FirstOrDefault()?.FirstOrDefault(), $"{blocks.Count} bloco(s)");

        if (field.Repeat)
        {
            var array = new JsonArray();
            for (var i = 0; i < blocks.Count; i++)
                array.Add(EvaluateFields(field.Fields!, blocks[i], $"{path}[{i}]"));
            return array;
        }

        var block = PickOccurrence(blocks, field.Occurrence);
        return block is null ? NotFound(field, path, "bloco não encontrado") : EvaluateFields(field.Fields!, block, path);
    }

    // =====================================================================
    // metadata / formField
    // =====================================================================

    private JsonNode? FromDictionary(FieldDefinition field, Dictionary<string, string?> source, string path) =>
        source.TryGetValue(field.Key!, out var value) && !string.IsNullOrEmpty(value)
            ? Finish(field, value, path, null)
            : NotFound(field, path, $"chave '{field.Key}' não encontrada");

    // =====================================================================
    // Busca de rótulos
    // =====================================================================

    private sealed class LabelMatch
    {
        public required TextLine Line { get; init; }
        public required int Start { get; init; }
        public required int End { get; init; }
        public List<WordBox> Words => Line.Words.Where(w => w.End > Start && w.Start < End).ToList();
        public double Left => Words.Min(w => w.Left);
        public double Right => Words.Max(w => w.Right);
        public double Bottom => Words.Min(w => w.Bottom);
    }

    private LabelMatch? FindLabel(FieldDefinition field, IReadOnlyList<TextLine> scope)
    {
        var lines = field.Page is { } p ? scope.Where(l => l.Page == p) : scope;
        IEnumerable<LabelMatch> matches = field.LabelPattern is not null
            ? lines.SelectMany(l => FindPatternInLine(l, GetRegex(field.LabelPattern, false)))
            : lines.SelectMany(l => FindInLine(l, field.Label!));

        return PickOccurrence(matches.ToList(), field.Occurrence);
    }

    /// <summary>Procura um texto na linha (ignorando acentos/caixa conforme settings) respeitando limites de palavra.</summary>
    private IEnumerable<LabelMatch> FindInLine(TextLine line, string label)
    {
        var needle = TextNormalizer.Normalize(TextNormalizer.CollapseSpaces(label), _settings.IgnoreAccents);
        if (needle.Length == 0) yield break;

        var comparison = _settings.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var text = line.NormalizedText;
        var from = 0;
        while (from <= text.Length - needle.Length)
        {
            var idx = text.IndexOf(needle, from, comparison);
            if (idx < 0) yield break;

            var end = idx + needle.Length;
            var startOk = idx == 0 || !char.IsLetterOrDigit(needle[0]) || !char.IsLetterOrDigit(text[idx - 1]);
            var endOk = end == text.Length || !char.IsLetterOrDigit(needle[^1]) || !char.IsLetterOrDigit(text[end]);
            if (startOk && endOk)
                yield return new LabelMatch { Line = line, Start = idx, End = end };
            from = idx + 1;
        }
    }

    private static IEnumerable<LabelMatch> FindPatternInLine(TextLine line, Regex regex) =>
        regex.Matches(line.NormalizedText)
            .Where(m => m.Success && m.Length > 0)
            .Select(m => new LabelMatch { Line = line, Start = m.Index, End = m.Index + m.Length });

    // =====================================================================
    // Pós-processamento do valor
    // =====================================================================

    private JsonNode? Finish(FieldDefinition field, string? raw, string path, TextLine? line)
    {
        AddTrace(path, field.Type, line, raw);
        return ProcessValue(raw, field, path);
    }

    private JsonNode? NotFound(FieldDefinition field, string path, string note)
    {
        if (_debug)
            _trace.Add(new TraceEntry { Field = path, Type = field.Type, Note = note });
        return ProcessValue(null, field, path);
    }

    /// <summary>Pipeline: default → limpa espaços → valuePattern → transforms → nullValues → conversão de tipo.</summary>
    private JsonNode? ProcessValue(string? raw, ValueOptions options, string path)
    {
        raw ??= options.Default;
        if (raw is null) return null;

        var value = TextNormalizer.CollapseSpaces(raw);

        if (!string.IsNullOrEmpty(options.ValuePattern))
        {
            var regex = GetRegex(options.ValuePattern, multiline: false);
            var m = regex.Match(TextNormalizer.Normalize(value, _settings.IgnoreAccents));
            if (!m.Success)
            {
                if (_debug) _trace.Add(new TraceEntry { Field = path, Type = "valuePattern", Raw = value, Note = "valuePattern não casou" });
                return options.Default is null ? null : ProcessValue(null, new ValueOptions { Default = options.Default, DataType = options.DataType, Format = options.Format }, path);
            }
            var g = m.Groups["value"].Success ? m.Groups["value"] : m.Groups.Count > 1 ? m.Groups[1] : m.Groups[0];
            value = value.Substring(g.Index, g.Length).Trim();
        }

        value = ValueConverter.ApplyTransforms(value, options.Transforms).Trim();

        if (value.Length == 0 || options.NullValues?.Any(n => n.Equals(value, StringComparison.OrdinalIgnoreCase)) == true)
            return null;

        if (ValueConverter.TryConvert(value, options, _culture, out var node, out var error))
            return node;

        _warnings.Add($"{path}: {error}");
        return null;
    }

    // =====================================================================
    // Utilitários
    // =====================================================================

    private IEnumerable<TextLine> FilterLines(IEnumerable<TextLine> lines, List<string>? ignore)
    {
        if (ignore is null || ignore.Count == 0) return lines;
        var regexes = ignore.Select(p => GetRegex(p, false)).ToList();
        return lines.Where(l => !regexes.Any(r => r.IsMatch(l.NormalizedText)));
    }

    private Regex GetRegex(string pattern, bool multiline)
    {
        if (_regexCache.TryGetValue((pattern, multiline), out var cached))
            return cached;

        var options = RegexOptions.CultureInvariant;
        if (!_settings.CaseSensitive) options |= RegexOptions.IgnoreCase;
        if (multiline) options |= RegexOptions.Multiline;

        // com IgnoreAccents o texto é comparado sem acentos, então o padrão também precisa estar sem acentos
        var normalized = TextNormalizer.Normalize(pattern, _settings.IgnoreAccents);
        return _regexCache[(pattern, multiline)] = new Regex(normalized, options, RegexTimeout);
    }

    private static T? PickOccurrence<T>(IReadOnlyList<T> items, int occurrence) where T : class
    {
        if (items.Count == 0) return null;
        var index = occurrence < 0 ? items.Count + occurrence : occurrence - 1;
        return index >= 0 && index < items.Count ? items[index] : null;
    }

    private static int IndexOf(IReadOnlyList<TextLine> scope, TextLine line)
    {
        for (var i = 0; i < scope.Count; i++)
            if (ReferenceEquals(scope[i], line)) return i;
        return -1;
    }

    private void AddTrace(string path, string type, TextLine? line, string? raw)
    {
        if (!_debug) return;
        _trace.Add(new TraceEntry
        {
            Field = path,
            Type = type,
            Page = line?.Page,
            Y = line is null ? null : Math.Round(line.Bottom, 1),
            Line = line?.Text,
            Raw = raw
        });
    }

    private static bool IsEmpty(JsonNode? node) => node switch
    {
        null => true,
        JsonArray a => a.Count == 0,
        _ => false
    };
}

/// <summary>
/// Decide a qual coluna uma palavra pertence:
/// 1º pela maior sobreposição horizontal com o cabeçalho da coluna;
/// 2º (sem sobreposição) pela coluna cujo início está mais próximo à esquerda do centro da palavra.
/// Valores costumam ser alinhados à direita e em fonte maior que o rótulo, por isso o 2º critério existe.
/// </summary>
public static class ColumnAssigner
{
    public static int Assign(WordBox word, IReadOnlyList<(double Left, double Right)> spans)
    {
        var best = -1;
        var bestOverlap = 0.0;
        for (var i = 0; i < spans.Count; i++)
        {
            var overlap = Math.Min(word.Right, spans[i].Right) - Math.Max(word.Left, spans[i].Left);
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = i;
            }
        }
        if (best >= 0) return best;

        var center = word.CenterX;
        var candidate = -1;
        var candidateLeft = double.MinValue;
        for (var i = 0; i < spans.Count; i++)
        {
            if (spans[i].Left <= center && spans[i].Left > candidateLeft)
            {
                candidate = i;
                candidateLeft = spans[i].Left;
            }
        }
        return candidate;
    }
}
