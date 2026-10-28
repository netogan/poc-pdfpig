using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PocPdfPig.Api.Services;

namespace PocPdfPig.Api.Controllers;

/// <summary>Templates prontos (pasta Templates) e validação de templates.</summary>
[ApiController]
[Route("api/templates")]
[Produces("application/json")]
public sealed class TemplatesController(TemplateStore store) : PdfControllerBase
{
    /// <summary>Lista os templates prontos disponíveis.</summary>
    [HttpGet]
    public ActionResult<IEnumerable<object>> List() =>
        Ok(store.ListTemplates().Select(name =>
        {
            var (template, _) = TemplateParser.Parse(store.ReadTemplate(name));
            return new { name, description = template?.Description };
        }));

    /// <summary>Retorna o JSON de um template pronto (copie e adapte para novos documentos).</summary>
    /// <param name="name">Nome do template. Ex.: dctfweb</param>
    [HttpGet("{name}")]
    public ActionResult Get(string name)
    {
        var json = store.ReadTemplate(name);
        return json is null
            ? Problem(title: $"Template '{name}' não encontrado", statusCode: 404)
            : Content(json, "application/json");
    }

    /// <summary>Valida um template sem precisar de um PDF.</summary>
    /// <remarks>Retorna 200 com "valid": true, ou 400 com a lista de problemas encontrados.</remarks>
    [HttpPost("validate")]
    [Consumes("application/json")]
    public ActionResult Validate([FromBody] JsonElement template)
    {
        var (parsed, errors) = TemplateParser.Parse(template.GetRawText());
        return parsed is null ? TemplateErrors(errors) : Ok(new { valid = true, fields = parsed.Fields.Count });
    }
}
