using System.Text.Json.Nodes;

namespace PocPdfPig.Api.Models;

/// <summary>Resposta do endpoint de extração.</summary>
public sealed class ExtractionResult
{
    /// <summary>Nome do template usado.</summary>
    public string? Template { get; set; }

    /// <summary>Nome do arquivo recebido.</summary>
    public string? FileName { get; set; }

    /// <summary>Total de páginas do PDF.</summary>
    public int PageCount { get; set; }

    /// <summary>false quando algum campo "required" não foi encontrado.</summary>
    public bool Success { get; set; }

    /// <summary>Tempo de processamento em milissegundos.</summary>
    public long ElapsedMs { get; set; }

    /// <summary>Dados extraídos, na mesma estrutura de "fields" do template.</summary>
    public JsonObject Data { get; set; } = new();

    /// <summary>Avisos: campos obrigatórios ausentes, falhas de conversão, colunas não encontradas etc.</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Rastro de onde cada valor foi encontrado (somente com debug=true).</summary>
    public List<TraceEntry>? Trace { get; set; }
}

/// <summary>Registro de depuração: onde e como um campo foi encontrado.</summary>
public sealed class TraceEntry
{
    public required string Field { get; init; }
    public required string Type { get; init; }
    public int? Page { get; init; }
    public double? Y { get; init; }
    public string? Line { get; init; }
    public string? Raw { get; init; }
    public string? Note { get; init; }
}
