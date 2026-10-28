using Microsoft.AspNetCore.Mvc;
using PocPdfPig.Api.Models;
using PocPdfPig.Api.Services;

namespace PocPdfPig.Api.Controllers;

/// <summary>Extração de dados de PDFs a partir de um template.</summary>
[ApiController]
[Route("api/extraction")]
[Produces("application/json")]
public sealed class ExtractionController(PdfExtractionService service, TemplateStore store) : PdfControllerBase
{
    /// <summary>Extrai dados de um PDF usando o template (JSON) enviado no mesmo request.</summary>
    /// <remarks>
    /// Envie como multipart/form-data:
    /// - file: o PDF
    /// - template: o JSON do template (ex.: conteúdo de GET /api/templates/darf-comprovante-arrecadacao)
    /// - debug: true para receber o "trace" de onde cada valor foi encontrado
    /// </remarks>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ExtractionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> Extract([FromForm] ExtractionRequest request)
    {
        if (ValidateFile(request.File) is { } fileError) return fileError;

        var (template, errors) = TemplateParser.Parse(request.Template);
        if (template is null) return TemplateErrors(errors);

        return RunExtraction(service, await ReadAsync(request.File), template, request.File.FileName, request.Debug);
    }

    /// <summary>Extrai dados de um PDF usando um template salvo na pasta Templates.</summary>
    /// <param name="templateName">Nome do template (veja GET /api/templates). Ex.: dctfweb</param>
    /// <param name="request">Arquivo e opções.</param>
    [HttpPost("{templateName}")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ExtractionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ExtractWithSavedTemplate(string templateName, [FromForm] SavedTemplateExtractionRequest request)
    {
        if (ValidateFile(request.File) is { } fileError) return fileError;

        var json = store.ReadTemplate(templateName);
        if (json is null) return Problem(title: $"Template '{templateName}' não encontrado", statusCode: 404);

        var (template, errors) = TemplateParser.Parse(json);
        if (template is null) return TemplateErrors(errors);

        return RunExtraction(service, await ReadAsync(request.File), template, request.File.FileName, request.Debug);
    }
}
