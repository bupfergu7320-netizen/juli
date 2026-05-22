using System.IO;
using System.Text.Json;
using JuliMvs.Core.Camera;

namespace JuliMvs.App.Services;

internal sealed class LocalAppSettingsStore
{
    private const string SettingsFileName = "appsettings.json";

    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public LocalAppSettingsStore(string baseDirectory, JsonSerializerOptions jsonOptions)
    {
        _settingsPath = Path.Combine(baseDirectory, "Data", "Config", SettingsFileName);
        _jsonOptions = jsonOptions;
    }

    public LocalAppSettings? Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<LocalAppSettings>(File.ReadAllText(_settingsPath), _jsonOptions);
    }

    public void Save(LocalAppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            _settingsPath,
            JsonSerializer.Serialize(settings, _jsonOptions));
    }
}

internal sealed record LocalAppSettings(
    string CameraIpAddress,
    string PlcIpAddress,
    int PlcPort,
    CameraAcquisitionSettings CameraSettings);
