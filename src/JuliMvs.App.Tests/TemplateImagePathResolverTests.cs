using JuliMvs.App.Services;

VerifyOldPublishedDataPathResolvesUnderCurrentBaseDirectory();
VerifyExistingPathIsPreserved();
VerifyUnmatchedMissingPathIsPreserved();

Console.WriteLine("Template image path resolver keeps template images portable across published folder copies.");

static void VerifyOldPublishedDataPathResolvesUnderCurrentBaseDirectory()
{
    var testRoot = CreateTempDirectory();
    var currentBaseDirectory = Path.Combine(testRoot, "JuliMvs_App_20260520_165347");
    var currentTemplatePath = Path.Combine(
        currentBaseDirectory,
        "Data",
        "Camera",
        "20260520",
        "camera-160941629.bmp");
    Directory.CreateDirectory(Path.GetDirectoryName(currentTemplatePath)!);
    File.WriteAllText(currentTemplatePath, "template image placeholder");

    var oldPublishedPath = Path.Combine(
        testRoot,
        "JuliMvs_App_20260520_155148",
        "Data",
        "Camera",
        "20260520",
        "camera-160941629.bmp");

    var resolver = new TemplateImagePathResolver(currentBaseDirectory);
    var resolved = resolver.ResolvePath(oldPublishedPath);

    AssertEqual(currentTemplatePath, resolved, "old published Data path");
}

static void VerifyExistingPathIsPreserved()
{
    var testRoot = CreateTempDirectory();
    var existingPath = Path.Combine(testRoot, "Data", "Camera", "image.bmp");
    Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
    File.WriteAllText(existingPath, "template image placeholder");

    var resolver = new TemplateImagePathResolver(testRoot);
    var resolved = resolver.ResolvePath(existingPath);

    AssertEqual(existingPath, resolved, "existing path");
}

static void VerifyUnmatchedMissingPathIsPreserved()
{
    var testRoot = CreateTempDirectory();
    var missingPath = Path.Combine(testRoot, "Legacy", "Camera", "image.bmp");

    var resolver = new TemplateImagePathResolver(testRoot);
    var resolved = resolver.ResolvePath(missingPath);

    AssertEqual(missingPath, resolved, "unmatched missing path");
}

static string CreateTempDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), "JuliMvs.App.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void AssertEqual(string? expected, string? actual, string name)
{
    if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'");
    }
}
