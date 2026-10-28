using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PocPdfPig.Api.Models;
using PocPdfPig.Api.Services;

namespace PocPdfPig.Api.Controllers;

/// <summary>
/// Atalhos para testar com os PDFs da pasta pdf-amostras SEM precisar fazer upload.
/// Ideal para o F5 do Visual Studio + Swagger.
/// </summary>
[ApiController]
[Route("api/samples")]
[Produces("application/json")]
public sealed class SamplesController(PdfExtractionService service, TemplateStore store) : PdfControllerBase
{
    /// <summary>Lista os PDFs de amostra disponíveis.</summary>
    [HttpGet]
    public ActionResult List() => Ok(new { folder = store.SamplesFolder, files = store.ListSamples() });

    /// <summary>Inspeciona ("raio-X") um PDF de amostra.</summary>
    /// <param name="fileName">Ex.: darf-irpj.pdf</param>
    /// <param name="pages">Ex.: "1" ou "1-2". Vazio = todas.</param>
    /// <param name="includeWords">Inclui palavras com coordenadas.</param>
    /// <param name="includeBlocks">Inclui blocos Docstrum.</param>
    [HttpGet("{fileName}/inspect")]
    [ProducesResponseType<InspectResult>(StatusCodes.Status200OK)]
    public ActionResult Inspect(string fileName, string? pages = null, bool includeWords = false, bool includeBlocks = false)
    {
        var pdf = store.ReadSample(fileName);
        if (pdf is null) return Problem(title: $"Amostra '{fileName}' não encontrada", statusCode: 404);
        try
        {
            return Ok(service.Inspect(pdf, fileName, new InspectOptions { Pages = pages, IncludeWords = includeWords, IncludeBlocks = includeBlocks }));
        }
        catch (Exception ex)
        {
            return PdfError(ex);
        }
    }

    /// <summary>Extrai uma amostra com um template pronto.</summary>
    /// <param name="fileName">Ex.: darf-irpj.pdf</param>
    /// <param name="templateName">Ex.: darf-comprovante-arrecadacao</param>
    /// <param name="debug">Inclui o trace.</param>
    [HttpPost("{fileName}/extract/{templateName}")]
    [ProducesResponseType<ExtractionResult>(StatusCodes.Status200OK)]
    public ActionResult ExtractWithSavedTemplate(string fileName, string templateName, bool debug = false)
    {
        var pdf = store.ReadSample(fileName);
        if (pdf is null) return Problem(title: $"Amostra '{fileName}' não encontrada", statusCode: 404);

        var json = store.ReadTemplate(templateName);
        if (json is null) return Problem(title: $"Template '{templateName}' não encontrado", statusCode: 404);

        var (template, errors) = TemplateParser.Parse(json);
        return template is null ? TemplateErrors(errors) : RunExtraction(service, pdf, template, fileName, debug);
    }

    /// <summary>Extrai uma amostra com um template enviado no corpo (JSON). Ótimo para ir ajustando um template novo.</summary>
    /// <param name="fileName">Ex.: dctfweb.pdf</param>
    /// <param name="template">Template JSON.</param>
    /// <param name="debug">Inclui o trace.</param>
    [HttpPost("{fileName}/extract")]
    [Consumes("application/json")]
    [ProducesResponseType<ExtractionResult>(StatusCodes.Status200OK)]
    public ActionResult ExtractWithCustomTemplate(string fileName, [FromBody] JsonElement template, bool debug = false)
    {
        var pdf = store.ReadSample(fileName);
        if (pdf is null) return Problem(title: $"Amostra '{fileName}' não encontrada", statusCode: 404);

        var (parsed, errors) = TemplateParser.Parse(template.GetRawText());
        return parsed is null ? TemplateErrors(errors) : RunExtraction(service, pdf, parsed, fileName, debug);
    }
}
