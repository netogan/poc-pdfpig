using Microsoft.AspNetCore.Mvc;
using PocPdfPig.Api.Models;
using PocPdfPig.Api.Services;

namespace PocPdfPig.Api.Controllers;

/// <summary>Comportamento comum: leitura do upload e tratamento de erros de template/PDF.</summary>
public abstract class PdfControllerBase : ControllerBase
{
    protected static async Task<byte[]> ReadAsync(IFormFile file)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return ms.ToArray();
    }

    protected ActionResult? ValidateFile(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return Problem(title: "Arquivo não informado", detail: "Envie o PDF no campo \"file\" (multipart/form-data).", statusCode: 400);
        return null;
    }

    protected ActionResult TemplateErrors(IEnumerable<string> errors) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["template"] = errors.ToArray() })
        {
            Title = "Template inválido",
            Detail = "Corrija os itens abaixo. Consulte docs/02-referencia-template.md."
        });

    protected ActionResult RunExtraction(PdfExtractionService service, byte[] pdf, ExtractionTemplate template, string? fileName, bool debug)
    {
        try
        {
            return Ok(service.Extract(pdf, template, fileName, debug));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return PdfError(ex);
        }
    }

    protected ActionResult PdfError(Exception ex) =>
        Problem(title: "Falha ao processar o PDF", detail: $"{ex.GetType().Name}: {ex.Message}", statusCode: 422);
}
