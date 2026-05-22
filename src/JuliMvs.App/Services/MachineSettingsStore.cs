using System.IO;
using System.Text.Json;
using JuliMvs.Core.Vision;
using JuliMvs.Plc;

namespace JuliMvs.App.Services;

internal sealed class MachineSettingsStore
{
    public const string FileName = "machine-settings.json";

    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public MachineSettingsStore(string baseDirectory, JsonSerializerOptions jsonOptions)
    {
        _settingsPath = Path.Combine(baseDirectory, "Data", "Config", FileName);
        _jsonOptions = jsonOptions;
    }

    public string SettingsPath => _settingsPath;

    public MachineSettingsLoadResult LoadOrDefault()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new MachineSettingsLoadResult(MachineSettings.Default, LoadedFromFile: false, Error: null);
            }

            var settings = JsonSerializer.Deserialize<MachineSettings>(File.ReadAllText(_settingsPath), _jsonOptions)
                ?? MachineSettings.Default;
            return new MachineSettingsLoadResult(settings, LoadedFromFile: true, Error: null);
        }
        catch (Exception ex)
        {
            return new MachineSettingsLoadResult(MachineSettings.Default, LoadedFromFile: false, ex);
        }
    }

    public void Save(MachineSettings settings)
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

    public MachineSettings ReadFrom(string path)
    {
        return JsonSerializer.Deserialize<MachineSettings>(File.ReadAllText(path), _jsonOptions)
            ?? throw new InvalidOperationException("标定配置文件为空或格式不正确。");
    }

    public void ExportTo(string destinationPath, MachineSettings currentSettings)
    {
        Save(currentSettings);
        File.Copy(_settingsPath, destinationPath, overwrite: true);
    }

    public MachineSettings ImportFrom(string sourcePath)
    {
        var settings = ReadFrom(sourcePath);
        Save(settings);
        return settings;
    }
}

internal sealed record MachineSettingsLoadResult(
    MachineSettings Settings,
    bool LoadedFromFile,
    Exception? Error);

internal sealed record MachineSettings
{
    public LensDistortionCalibration? LensDistortionCalibration { get; init; } =
        JuliMvs.Core.Vision.LensDistortionCalibration.Disabled;

    public CameraCalibration CameraCalibration { get; init; } = CameraCalibration.Disabled;

    public RAxisCenterCalibration? RAxisCenterCalibration { get; init; } =
        JuliMvs.Core.Vision.RAxisCenterCalibration.Disabled;

    public bool InvertXCompensation { get; init; }

    public bool InvertYCompensation { get; init; }

    public bool InvertRotationCompensation { get; init; }

    public bool BackSideNgEnabled { get; init; }

    public double BackSideNgMinimumBackScore { get; init; } =
        VisionParameters.Default.BackSideNgMinimumBackScore;

    public double BackSideNgMaximumScoreDifference { get; init; } =
        VisionParameters.Default.BackSideNgMaximumScoreDifference;

    public PlcOutputTransform? PlcOutputTransform { get; init; } = JuliMvs.Plc.PlcOutputTransform.Identity;

    public static MachineSettings Default { get; } = new();
}
