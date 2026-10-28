using Microsoft.Extensions.Options;

namespace PocPdfPig.Api.Services;

public sealed class PocOptions
{
    /// <summary>Pasta com os templates prontos (*.json). Relativa à raiz do projeto da API.</summary>
    public string TemplatesFolder { get; set; } = "Templates";

    /// <summary>Pasta com os PDFs de amostra. Relativa à raiz do projeto da API.</summary>
    public string SamplesFolder { get; set; } = "../../pdf-amostras";
}

/// <summary>Acesso aos templates prontos (pasta Templates) e aos PDFs de amostra (pasta pdf-amostras).</summary>
public sealed class TemplateStore
{
    private readonly string _templatesFolder;
    private readonly string _samplesFolder;

    public TemplateStore(IOptions<PocOptions> options, IWebHostEnvironment env)
    {
        _templatesFolder = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.TemplatesFolder));
        _samplesFolder = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.SamplesFolder));
    }

    public string SamplesFolder => _samplesFolder;

    public IEnumerable<string> ListTemplates() =>
        Directory.Exists(_templatesFolder)
            ? Directory.GetFiles(_templatesFolder, "*.json").Select(Path.GetFileNameWithoutExtension).Order()!
            : Enumerable.Empty<string>();

    public string? ReadTemplate(string name) => ReadSafe(_templatesFolder, name.EndsWith(".json") ? name : name + ".json", File.ReadAllText);

    public IEnumerable<string> ListSamples() =>
        Directory.Exists(_samplesFolder)
            ? Directory.GetFiles(_samplesFolder, "*.pdf").Select(Path.GetFileName).Order()!
            : Enumerable.Empty<string>();

    public byte[]? ReadSample(string fileName) => ReadSafe(_samplesFolder, fileName.EndsWith(".pdf") ? fileName : fileName + ".pdf", File.ReadAllBytes);

    // impede "path traversal" (ex.: ../../arquivo)
    private static T? ReadSafe<T>(string folder, string fileName, Func<string, T> reader) where T : class
    {
        var path = Path.GetFullPath(Path.Combine(folder, fileName));
        if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            return null;
        return reader(path);
    }
}
