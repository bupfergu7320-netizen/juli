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
        return File.Exists(candidatePath)
            ? candidatePath
            : imagePath;
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
}
