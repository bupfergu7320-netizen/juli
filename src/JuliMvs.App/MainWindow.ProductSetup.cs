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
    private async Task TryEnterStartupProductionModeAsync()
    {
        var latestTemplate = await _repository.LoadMostRecentTemplateAsync();
        if (latestTemplate is null || string.IsNullOrWhiteSpace(latestTemplate.ProductName))
        {
            await TryEnterSimpleProductionModeAsync();
            return;
        }

        var productName = latestTemplate.ProductName.Trim();
        _currentProductName = productName;
        Log($"启动自动加载最近模板: 型号 {productName}, 模板时间 {latestTemplate.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        await TryEnterSimpleProductionModeAsync(productName);
    }

    private async Task TryEnterSimpleProductionModeAsync(string? preferredProductName = null)
    {
        var productName = (preferredProductName ?? _currentProductName).Trim();
        if (string.IsNullOrWhiteSpace(productName))
        {
            productName = DefaultProductName;
            _currentProductName = productName;
        }

        try
        {
            var batchNo = BatchNumberGenerator.GenerateDefaultBatchNo();
            if (VisionJudgmentDisabled)
            {
                StartVisionJudgmentBypassBatch(batchNo, productName);
                await TryLoadBypassLogReferenceTemplateAsync(productName);
                _productionEnabled = true;
                UpdateRunStopUi();
                SaveLocalSettings();
                Log($"无视觉判断生产模式已就绪: 型号 {productName}, 批次 {batchNo}, 等待PLC触发D1000=1。");
                return;
            }

            if (await StartBatchWithLatestTemplateAsync(batchNo, productName))
            {
                _productionEnabled = true;
                UpdateRunStopUi();
                SaveLocalSettings();
                Log($"最简生产模式已就绪: 型号 {productName}, 批次 {batchNo}, 等待PLC触发D1000=1。");
            }
        }
        catch (Exception ex)
        {
            _productionEnabled = false;
            UpdateRunStopUi();
            MessageText.Text = $"最简生产模式未就绪: {ex.Message}";
            Log(MessageText.Text);
        }
    }

    private void StartVisionJudgmentBypassBatch(string batchNo, string productName)
    {
        if (_batchSession.CanEnd)
        {
            var endedBatch = _batchSession.BatchNo;
            _batchSession.End();
            Log($"进入无视觉判断模式前结束当前批次: {endedBatch}");
        }

        _currentBatchNo = batchNo;
        _currentProductName = productName.Trim();
        _batchSession = BatchSession.Empty();
        _batchSession.Start(batchNo, _currentProductName);
        _batchSession.MarkTemplateCreated();
        _batchSession.ConfirmFirstArticle();
        ResetProductionCounters();
        ClearCurrentInspection();
        _changeoverTemplateRequested = false;
        MessageText.Text = "生产模式已就绪：执行拍照、显示、PLC通信；勾选反面NG时启用正反面检测，OK时输出XYR纠偏。";
    }

    private async Task TryLoadBypassLogReferenceTemplateAsync(string productName)
    {
        _bypassLogTemplateFeature = null;
        _bypassLogTemplateImagePath = null;
        _bypassLogTemplateProductName = null;

        try
        {
            var loadedTemplate = await _repository.LoadLatestTemplateAsync(productName);
            if (loadedTemplate is null)
            {
                Log($"日志正反面参考模板未加载: 型号 {productName} 没有模板，只记录当前轮廓形态。");
                return;
            }

            var template = _templateImagePathResolver.Resolve(loadedTemplate);
            if (string.IsNullOrWhiteSpace(template.ImagePath) || !File.Exists(template.ImagePath))
            {
                Log($"日志正反面参考模板未加载: 模板图片不存在 {template.ImagePath}");
                return;
            }

            if (!string.Equals(template.ImagePath, loadedTemplate.ImagePath, StringComparison.OrdinalIgnoreCase))
            {
                Log($"日志正反面参考模板图片已重定位: {loadedTemplate.ImagePath} -> {template.ImagePath}");
            }

            using var templateImage = new Mat(template.ImagePath, ImreadModes.Grayscale);
            if (templateImage.Empty())
            {
                Log($"日志正反面参考模板未加载: 模板图片读取失败 {template.ImagePath}");
                return;
            }

            var recipeLoaded = await LoadRecipeAsync(productName, showMessageWhenMissing: false);
            if (!recipeLoaded)
            {
                ApplyRecipeVisionParameters(template.Parameters);
                SetChangeoverBackSideNgCheckBox(_visionParameters.BackSideNgEnabled);
                SetChangeoverFourWaySymmetricCheckBox(_visionParameters.FourWaySymmetricEnabled);
            }

            var activeParameters = ReadVisionParameters();
            _bypassLogTemplateFeature = _contourFeatureExtractor.Extract(templateImage, activeParameters);
            _bypassLogTemplateImagePath = template.ImagePath;
            _bypassLogTemplateProductName = productName.Trim();
            _template = template with
            {
                BatchNo = _currentBatchNo,
                ProductName = productName.Trim(),
                Roi = activeParameters.Roi,
                Parameters = activeParameters
            };
            _templateImagePath = template.ImagePath;
            WarmupProductionTemplate(_template, activeParameters);
            Log(
                $"日志正反面参考模板已加载: 型号 {productName}, " +
                $"图片 {template.ImagePath}, " +
                $"模板形态 {FormatAutoPartShapeClass(_bypassLogTemplateFeature.Strategy.ShapeClass)}, " +
                $"半径特征 {_bypassLogTemplateFeature.RadiusSignalPixels:F2}px, " +
                $"四边对称{FormatRecipeFourWaySymmetricState(_visionParameters)}。");
        }
        catch (Exception ex)
        {
            Log($"正反面参考模板加载失败: {ex.Message}。生产流程不受影响，本次无法执行正反面对比。");
        }
    }

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
        _currentProductName = productName.Trim();
        _batchSession = BatchSession.Empty();
        _batchSession.Start(batchNo, productName);
        ResetProductionCounters();
        ClearCurrentInspection();
        _changeoverTemplateRequested = false;

        var templateLoaded = await LoadProductSetupForBatchAsync(batchNo, productName);
        if (templateLoaded)
        {
            _batchSession.MarkTemplateCreated();
            _batchSession.ConfirmFirstArticle();
            SaveLocalSettings();
            MessageText.Text = "产品配方和标准位/模板已加载，等待PLC触发拍照检测。";
            return true;
        }

        MessageText.Text = "当前型号没有标准位/模板。请点击“换型”，放入标准件后由上位机拍照重新建立标准位/模板。";
        Log($"未找到产品模板: {productName}");
        return false;
    }

    private async Task<bool> LoadProductSetupForBatchAsync(string batchNo, string productName)
    {
        var recipeLoaded = await LoadRecipeAsync(productName, showMessageWhenMissing: false);

        var loadedTemplate = await _repository.LoadLatestTemplateAsync(productName);
        if (loadedTemplate is null)
        {
            return false;
        }

        var template = _templateImagePathResolver.Resolve(loadedTemplate);
        if (!string.Equals(template.ImagePath, loadedTemplate.ImagePath, StringComparison.OrdinalIgnoreCase))
        {
            Log($"模板图片路径已重定位: {loadedTemplate.ImagePath} -> {template.ImagePath}");
        }

        if (!recipeLoaded)
        {
            ApplyRecipeVisionParameters(loadedTemplate.Parameters);
            SetChangeoverBackSideNgCheckBox(_visionParameters.BackSideNgEnabled);
            SetChangeoverFourWaySymmetricCheckBox(_visionParameters.FourWaySymmetricEnabled);
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
        LoadProductionContourReferenceTemplateFromFile(_template, activeParameters);
        RenderTemplateSummary(_template);
        WarmupProductionTemplate(_template, activeParameters);
        Log($"产品模板已加载: {productName}, 来源批次: {template.BatchNo}, 模板时间: {template.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        return true;
    }

    private void WarmupProductionTemplate(PartTemplate template, VisionParameters parameters)
    {
        var startedAt = DateTimeOffset.Now;
        try
        {
            _visionService.WarmupProductionTemplate(template, parameters);

            if (_bypassLogTemplateFeature is not null)
            {
                _productionAutoAngleResolver.WarmupTemplate(
                    _bypassLogTemplateFeature,
                    parameters.BackSideNgEnabled);
                _productionMissingMaterialDetector.WarmupTemplate(
                    template,
                    _bypassLogTemplateFeature);

                var selfAngle = _productionAutoAngleResolver.Resolve(
                    _bypassLogTemplateFeature,
                    _bypassLogTemplateFeature,
                    template,
                    fourWaySymmetric: parameters.FourWaySymmetricEnabled);
                if (selfAngle.IsReliable)
                {
                    _ = _productionMissingMaterialDetector.Evaluate(
                        _bypassLogTemplateFeature,
                        _bypassLogTemplateFeature,
                        template,
                        selfAngle);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"生产模板缓存预热失败: {ex.Message}。首件可能稍慢，检测流程继续。");
            return;
        }
        var elapsedMs = (long)(DateTimeOffset.Now - startedAt).TotalMilliseconds;
        Log($"生产模板缓存已预热: {elapsedMs}ms，首件PLC触发无需再初始化模板/角度缓存。");
    }

    private void LoadProductionContourReferenceTemplateFromFile(
        PartTemplate template,
        VisionParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(template.ImagePath) || !File.Exists(template.ImagePath))
        {
            throw new InvalidOperationException($"模板图片不存在: {template.ImagePath}");
        }

        using var templateImage = new Mat(template.ImagePath, ImreadModes.Grayscale);
        if (templateImage.Empty())
        {
            throw new InvalidOperationException($"模板图片读取失败: {template.ImagePath}");
        }

        _bypassLogTemplateFeature = _contourFeatureExtractor.Extract(templateImage, parameters);
        _bypassLogTemplateImagePath = template.ImagePath;
        _bypassLogTemplateProductName = template.ProductName.Trim();
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
        var runtimeParameters = ReadVisionParameters();
        var parameters = _template is not null &&
            string.Equals(_template.ProductName, productName.Trim(), StringComparison.OrdinalIgnoreCase)
            ? ProductRecipeVisionParameters.ForSave(_template, runtimeParameters)
            : ProductRecipeVisionParameters.ForSave(runtimeParameters);
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
            _visionParameters = _visionParameters with
            {
                BackSideNgEnabled = false,
                FourWaySymmetricEnabled = false,
                FrontBumpFeature = FrontBumpFeature.Disabled
            };
            SetChangeoverBackSideNgCheckBox(false);
            SetChangeoverFourWaySymmetricCheckBox(false);
            if (showMessageWhenMissing)
            {
                MessageBox.Show($"未找到型号配方: {productName}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return false;
        }

        ApplyRecipeVisionParameters(recipe.VisionParameters);
        SetChangeoverBackSideNgCheckBox(_visionParameters.BackSideNgEnabled);
        SetChangeoverFourWaySymmetricCheckBox(_visionParameters.FourWaySymmetricEnabled);
        _cameraSettings = recipe.CameraSettings;
        SaveLocalSettings();
        await ApplyCameraSettingsToConnectedCameraAsync();
        Log(
            $"型号配方已加载: {productName}，" +
            $"曝光{_cameraSettings.ExposureTimeMicroseconds:F1}us，" +
            $"增益{_cameraSettings.Gain:F1}，" +
            $"曝光延迟{_cameraSettings.CaptureDelaySeconds:F3}s，" +
            $"反面NG{FormatRecipeBackSideNgState(_visionParameters)}，" +
            $"四边对称{FormatRecipeFourWaySymmetricState(_visionParameters)}");
        return true;
    }

    private static string FormatRecipeBackSideNgState(VisionParameters parameters)
    {
        if (!parameters.BackSideNgEnabled)
        {
            return "未启用";
        }

        return "已启用，轮廓采样镜像匹配";
    }

    private static string FormatRecipeFourWaySymmetricState(VisionParameters parameters)
    {
        return parameters.FourWaySymmetricEnabled
            ? "已启用，R按180°等价"
            : "未启用";
    }
}
