using System.ComponentModel;

namespace PocPdfPig.Api.Models;

/// <summary>Extração com template enviado junto com o arquivo.</summary>
public sealed class ExtractionRequest
{
    /// <summary>Arquivo PDF a ser extraído.</summary>
    public IFormFile File { get; set; } = default!;

    /// <summary>Template (JSON) com os campos a extrair. Veja GET /api/templates para exemplos prontos.</summary>
    public string Template { get; set; } = "";

    /// <summary>true = inclui "trace" com a página/linha onde cada valor foi encontrado.</summary>
    [DefaultValue(false)]
    public bool Debug { get; set; }
}

/// <summary>Extração usando um template salvo na pasta Templates.</summary>
public sealed class SavedTemplateExtractionRequest
{
    /// <summary>Arquivo PDF a ser extraído.</summary>
    public IFormFile File { get; set; } = default!;

    /// <summary>true = inclui "trace" com a página/linha onde cada valor foi encontrado.</summary>
    [DefaultValue(false)]
    public bool Debug { get; set; }
}

/// <summary>Inspeção ("raio-X") de um PDF.</summary>
public sealed class InspectRequest
{
    /// <summary>Arquivo PDF a inspecionar.</summary>
    public IFormFile File { get; set; } = default!;

    /// <summary>Páginas (ex.: "1", "1-3"). Vazio = todas.</summary>
    public string? Pages { get; set; }

    /// <summary>Inclui as palavras com coordenadas.</summary>
    [DefaultValue(false)]
    public bool IncludeWords { get; set; }

    /// <summary>Inclui os blocos de texto detectados pelo Docstrum (PdfPig).</summary>
    [DefaultValue(false)]
    public bool IncludeBlocks { get; set; }

    /// <summary>Inclui o texto na ordem de conteúdo do arquivo (ContentOrderTextExtractor).</summary>
    [DefaultValue(false)]
    public bool IncludeContentOrderText { get; set; }

    /// <summary>Tolerância vertical para agrupar palavras na mesma linha (padrão 3).</summary>
    [DefaultValue(3)]
    public double LineTolerance { get; set; } = 3;

    /// <summary>Espaço horizontal mínimo para separar células (padrão 8).</summary>
    [DefaultValue(8)]
    public double CellGap { get; set; } = 8;
}
