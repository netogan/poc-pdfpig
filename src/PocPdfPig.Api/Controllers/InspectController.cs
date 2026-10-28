using Microsoft.AspNetCore.Mvc;
using PocPdfPig.Api.Models;
using PocPdfPig.Api.Services;

namespace PocPdfPig.Api.Controllers;

/// <summary>"Raio-X" do PDF: linhas, células, palavras e coordenadas. Primeiro passo para criar um template.</summary>
[ApiController]
[Route("api/inspect")]
[Produces("application/json")]
public sealed class InspectController(PdfExtractionService service) : PdfControllerBase
{
    /// <summary>Mostra como o motor enxerga o PDF (texto por linha, células e coordenadas).</summary>
    /// <remarks>
    /// Use a propriedade pages[].text para escrever regex e pages[].lines[].cells para entender
    /// como os rótulos e valores estão distribuídos horizontalmente.
    /// </remarks>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<InspectResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Inspect([FromForm] InspectRequest request)
    {
        if (ValidateFile(request.File) is { } fileError) return fileError;

        try
        {
            var options = new InspectOptions
            {
                Pages = request.Pages,
                IncludeWords = request.IncludeWords,
                IncludeBlocks = request.IncludeBlocks,
                IncludeContentOrderText = request.IncludeContentOrderText,
                LineTolerance = request.LineTolerance,
                CellGap = request.CellGap
            };
            return Ok(service.Inspect(await ReadAsync(request.File), request.File.FileName, options));
        }
        catch (Exception ex)
        {
            return PdfError(ex);
        }
    }
}
