using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JuliMvs.Camera.Hik;
using JuliMvs.Core.Batch;
using JuliMvs.Core.Camera;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Persistence;
using JuliMvs.Core.Vision;
using JuliMvs.App.Services;
using JuliMvs.Persistence;
using JuliMvs.Plc;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App;

public partial class MainWindow
{
    private async Task<bool> StartBatchWithLatestTemplateAsync(string batchNo, string productName)
    {
        RequireMachineCalibrationReady();

        if (_batchSession.CanEnd)
        {
            var endedBatch = _batchSession.BatchNo;
            _batchSession.End();
            Log($"切换型号前结束当前批次: {endedBatch}");
        }

        _currentBatchNo = batchNo;
        _batchSession = BatchSession.Empty();
        _batchSession.Start(batchNo, productName);
        ClearCurrentInspection();
        _changeoverTemplateRequested = false;

        var templateLoaded = await LoadProductSetupForBatchAsync(batchNo, productName);
        if (templateLoaded)
        {
            _batchSession.MarkTemplateCreated();
            _batchSession.ConfirmFirstArticle();
            MessageText.Text = "产品配方和标准位/模板已加载，等待PLC触发拍照检测。";
            return true;
        }

        MessageText.Text = "当前型号没有标准位/模板。请点击“换型”，放入标准件后由上位机拍照重新建立标准位/模板。";
        Log($"未找到产品模板: {productName}");
        return false;
    }

    private async Task<bool> LoadProductSetupForBatchAsync(string batchNo, string productName)
    {
        await LoadRecipeAsync(productName, showMessageWhenMissing: false);

        var template = await _repository.LoadLatestTemplateAsync(productName);
        if (template is null)
        {
            return false;
        }

        var activeParameters = ReadVisionParameters();
        var setup = OpenCvVisionService.ValidateProductionSetup(template, activeParameters);
        if (!setup.IsReady)
        {
            throw new InvalidOperationException(ProductionSetupMessageFormatter.FormatBlockMessage(setup.Reason));
        }

        _template = template with
        {
            BatchNo = batchNo,
            ProductName = productName,
            Roi = activeParameters.Roi,
            Parameters = activeParameters
        };
        _templateImagePath = template.ImagePath;
        RenderTemplateSummary(_template);
        Log($"产品模板已加载: {productName}, 来源批次: {template.BatchNo}, 模板时间: {template.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        return true;
    }

    private void RequireMachineCalibrationReady()
    {
        if (!IsMachineCalibrationReady(out var message))
        {
            throw new InvalidOperationException(message);
        }
    }

    private bool IsMachineCalibrationReady(out string message)
    {
        var readiness = _machineCalibrationRuntime.EvaluateMachineReadiness(ReadVisionParameters());
        if (!readiness.IsReady)
        {
            message = readiness.Message;
            return false;
        }

        message = string.Empty;
        return true;
    }

    private Task SaveRecipeAsync(string productName)
    {
        var parameters = ProductRecipeVisionParameters.ForSave(ReadVisionParameters());
        var recipe = new ProductRecipe
        {
            VisionParameters = parameters,
            CameraSettings = _cameraSettings
        };
        return _repository.SaveProductRecipeAsync(productName, recipe);
    }

    private async Task<bool> LoadRecipeAsync(string productName, bool showMessageWhenMissing)
    {
        var recipe = await _repository.LoadProductRecipeAsync(productName);
        if (recipe is null)
        {
            if (showMessageWhenMissing)
            {
                MessageBox.Show($"未找到型号配方: {productName}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return false;
        }

        ApplyRecipeVisionParameters(recipe.VisionParameters);
        _cameraSettings = recipe.CameraSettings;
        SaveLocalSettings();
        await ApplyCameraSettingsToConnectedCameraAsync();
        Log(
            $"型号配方已加载: {productName}，" +
            $"曝光{_cameraSettings.ExposureTimeMicroseconds:F1}us，" +
            $"增益{_cameraSettings.Gain:F1}，" +
            $"曝光延迟{_cameraSettings.CaptureDelaySeconds:F3}s");
        return true;
    }
}
