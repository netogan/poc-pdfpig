namespace PocPdfPig.Tests;

/// <summary>
/// Os PDFs de amostra contêm dados fiscais reais e NÃO são versionados (pasta pdf-amostras/ está no .gitignore).
/// Testes marcados com estes atributos são pulados automaticamente quando a pasta não existe.
/// </summary>
internal static class SampleFiles
{
    public static readonly string? Folder = FindFolder();

    public const string SkipReason =
        "Pasta pdf-amostras/ não encontrada. Copie os PDFs de amostra para a raiz do repositório para rodar este teste.";

    private static string? FindFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PocPdfPig.sln")))
            dir = dir.Parent;
        var folder = dir is null ? null : Path.Combine(dir.FullName, "pdf-amostras");
        return folder is not null && Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.pdf").Any() ? folder : null;
    }
}

public sealed class SampleFactAttribute : FactAttribute
{
    public SampleFactAttribute()
    {
        if (SampleFiles.Folder is null) Skip = SampleFiles.SkipReason;
    }
}

public sealed class SampleTheoryAttribute : TheoryAttribute
{
    public SampleTheoryAttribute()
    {
        if (SampleFiles.Folder is null) Skip = SampleFiles.SkipReason;
    }
}
