using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using PocPdfPig.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PocOptions>(builder.Configuration.GetSection("Poc"));
builder.Services.AddSingleton<TemplateStore>();
builder.Services.AddSingleton<PdfExtractionService>();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        // templates enviados no corpo podem ter comentários e vírgula sobrando
        o.JsonSerializerOptions.ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip;
        o.JsonSerializerOptions.AllowTrailingCommas = true;
    });
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "POC PdfPig - Extração de documentos fiscais",
        Version = "v1",
        Description = """
            Extrai dados de PDFs (DARF, DCTF, DCTFWeb, DCOMP, NFS-e, DI...) a partir de um **template JSON**.

            Fluxo sugerido para um documento novo:
            1. `GET /api/samples` ou faça upload em `POST /api/inspect` para ver como o PDF é lido (linhas, células, coordenadas);
            2. copie um template parecido em `GET /api/templates/{name}`;
            3. ajuste e teste em `POST /api/samples/{fileName}/extract` (template no corpo) ou `POST /api/extraction` (upload);
            4. use `debug=true` para ver onde cada valor foi encontrado.

            Documentação completa na pasta **docs/** do repositório.
            """
    });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) c.IncludeXmlComments(xml);
});

var app = builder.Build();

// POC: Swagger habilitado em qualquer ambiente
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.DocumentTitle = "POC PdfPig";
    c.DisplayRequestDuration();
});

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Exposto para testes de integração.</summary>
public partial class Program;
