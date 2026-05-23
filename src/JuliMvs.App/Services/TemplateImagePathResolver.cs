using JuliMvs.Core.Vision;
using System.IO;

namespace JuliMvs.App.Services;

internal sealed class TemplateImagePathResolver
{
    private readonly string _baseDirectory;

    public TemplateImagePathResolver(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = Path.GetFullPath(baseDirectory);
    }

    public PartTemplate Resolve(PartTemplate template)
    {
        var resolvedPath = ResolvePath(template.ImagePath);
        return string.Equals(resolvedPath, template.ImagePath, StringComparison.OrdinalIgnoreCase)
            ? template
            : template with { ImagePath = resolvedPath };
    }

    public string? ResolvePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return imagePath;
        }

        if (File.Exists(imagePath))
        {
            return imagePath;
        }

        var dataRelativePath = TryGetDataRelativePath(imagePath);
        if (dataRelativePath is null)
        {
            return imagePath;
        }

        var candidatePath = Path.Combine(_baseDirectory, "Data", dataRelativePath);
        if (File.Exists(candidatePath))
        {
            return candidatePath;
        }

        var migratedTemplatePath = TryFindMigratedTemplatePath(imagePath);
        return migratedTemplatePath ?? imagePath;
    }

    private static string? TryGetDataRelativePath(string imagePath)
    {
        var parts = imagePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (string.Equals(parts[index], "Data", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(parts[(index + 1)..]);
            }
        }

        return null;
    }

    private string? TryFindMigratedTemplatePath(string imagePath)
    {
        var fileName = Path.GetFileName(imagePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var templatesDirectory = Path.Combine(_baseDirectory, "Data", "Templates");
        if (!Directory.Exists(templatesDirectory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(templatesDirectory, fileName, SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
