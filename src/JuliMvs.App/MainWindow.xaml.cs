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

public partial class MainWindow : System.Windows.Window
{
    private const int RequiredCalibrationPointCount = 9;
    private const double DefaultCalibrationStepMm = 30.0;
    private const int CalibrationBoardRows = 7;
    private const int CalibrationBoardColumns = 7;
    private const double CalibrationBoardSpacingMm = 10.0;
    private const string RAxisCenterCaptureTargetBoard = "7x7-calibration-board-center-dot";
    private const string DefaultCameraIpAddress = "192.168.10.11";
    private const string DefaultPlcIpAddress = "192.168.3.40";
    private const string DefaultProductName = "PART-A";
    private const int DefaultPlcPort = 502;
    private const int CameraCaptureTimeoutMilliseconds = 1200;
    private const double MaximumAcceptedLensDistortionRmsPixels = 0.60;
    private const double MaximumAcceptedCameraCalibrationRmsMm = 0.10;
    private const int MinimumAcceptedRAxisCenterPointCount = 5;
    private const double MinimumAcceptedRAxisCenterAngleCoverageDegrees = 180.0;
    private const double MaximumAcceptedRAxisCenterRmsMm = 0.05;
    private const double MaximumAcceptedRAxisCenterMaxMm = 0.10;

    private static readonly JsonSerializerOptions LocalJsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private static readonly string[] ChangeoverStepLabels =
    [
        "确认型号",
        "放入标准件",
        "上位机拍照",
        "建立标准位/模板",
        "完成换型"
    ];

    private readonly OpenCvVisionService _visionService = new();
    private readonly ContourFeatureExtractor _contourFeatureExtractor = new();
    private readonly ProductionAutoAngleResolver _productionAutoAngleResolver = new();
    private readonly ProductionMissingMaterialDetector _productionMissingMaterialDetector = new();
    private readonly CalibrationBoardVisionService _calibrationBoardVisionService = new();
    private readonly LensDistortionCalibrationService _lensDistortionCalibrationService = new();
    private readonly CombinedCalibrationService _combinedCalibrationService = new();
    private readonly CalibrationQualityEvaluator _calibrationQualityEvaluator = new(
        new CalibrationQualityThresholds(
            MaximumAcceptedLensDistortionRmsPixels,
            MaximumAcceptedCameraCalibrationRmsMm,
            RequiredCalibrationPointCount,
            MinimumAcceptedRAxisCenterPointCount,
            MinimumAcceptedRAxisCenterAngleCoverageDegrees,
            MaximumAcceptedRAxisCenterRmsMm,
            MaximumAcceptedRAxisCenterMaxMm));
    private readonly CalibrationEditorSolver _calibrationEditorSolver = new();
    private readonly HikCameraService _cameraService = new();
    private readonly IInspectionRepository _repository;
    private readonly FileLogger _fileLogger;
    private readonly InspectionFileStore _inspectionFileStore;
    private readonly InspectionReportWriter _inspectionReportWriter;
    private readonly ChangeoverTemplateReportWriter _changeoverTemplateReportWriter;
    private readonly TemplateImagePathResolver _templateImagePathResolver;
    private readonly MachineSettingsStore _machineSettingsStore;
    private readonly LocalAppSettingsStore _localAppSettingsStore;
    private readonly CalibrationFileStore _calibrationFileStore;
    private readonly PlcInspectionResultWriter _plcInspectionResultWriter = new();
    private readonly PlcOutputDiagnosticFormatter _plcOutputDiagnosticFormatter = new();
    private readonly InspectionDiagnosticMessageFormatter _inspectionDiagnosticMessageFormatter = new();
    private readonly MachineCalibrationRuntime _machineCalibrationRuntime =
        new(RAxisCenterCaptureTargetBoard);
    private readonly PlcTriggerGate _plcTriggerGate = new();
    private readonly PlcPollingCoordinator _plcPollingCoordinator;
    private readonly PlcCaptureRequestValidator _plcCaptureRequestValidator = new();
    private readonly InspectionRunCoordinator _inspectionRunCoordinator;
    private readonly ReportSaveService _reportSaveService;
    private BatchSession _batchSession = BatchSession.Empty();
    private PartTemplate? _template;
    private string? _templateImagePath;
    private bool _cameraConnected;
    private CameraDeviceInfo? _connectedCameraInfo;
    private CameraAcquisitionSettings _cameraSettings = CameraAcquisitionSettings.Default;
    private string _currentBatchNo = string.Empty;
    private string _currentProductName = DefaultProductName;
    private string _cameraIpAddress = DefaultCameraIpAddress;
    private string _plcIpAddress = DefaultPlcIpAddress;
    private int _plcPort = DefaultPlcPort;
    private VisionParameters _visionParameters = VisionParameters.Default;
    private CameraCalibration _cameraCalibration = CameraCalibration.Disabled;
    private Mat? _lastCameraImage;
    private LensDistortionCalibration _lensDistortionCalibration = LensDistortionCalibration.Disabled;
    private RAxisCenterCalibration _rAxisCenterCalibration = RAxisCenterCalibration.Disabled;
    private PlcOutputTransform _plcOutputTransform = PlcOutputTransform.Identity;
    private InspectionResult? _lastInspectionResult;
    private string? _lastRawImagePath;
    private Mat? _pendingProductionNgDiagnosticOverlay;
    private ContourFeatureExtraction? _bypassLogTemplateFeature;
    private string? _bypassLogTemplateImagePath;
    private string? _bypassLogTemplateProductName;
    private MitsubishiModbusTcpPlcClient? _plcClient;
    private CancellationTokenSource? _plcPollingCts;
    private bool _productionEnabled;
    private bool _changeoverTemplateRequested;
    private int _productionTotalCount;
    private int _productionOkCount;
    private int _productionNgCount;
    private System.Windows.Window? _changeoverDialog;
    private TextBox? _changeoverModelBox;
    private TextBlock? _changeoverStatusText;
    private TextBlock? _changeoverHintText;
    private TextBlock? _changeoverSummaryText;
    private ComboBox? _changeoverTemplateSelector;
    private CheckBox? _changeoverBackSideNgCheckBox;
    private CheckBox? _changeoverFourWaySymmetricCheckBox;
    private bool _changeoverBackSideNgUserEdited;
    private bool _changeoverFourWaySymmetricUserEdited;
    private string? _changeoverBackSideNgEditProductName;
    private string? _changeoverFourWaySymmetricEditProductName;
    private bool _updatingChangeoverBackSideNgCheckBox;
    private bool _updatingChangeoverFourWaySymmetricCheckBox;
    private Button? _changeoverStartButton;
    private Button? _changeoverCaptureTemplateButton;
    private Button? _changeoverCancelButton;
    private readonly List<TextBlock> _changeoverStepTexts = [];
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private UserRole _currentUserRole = UserRole.Operator;

    public MainWindow()
    {
        InitializeComponent();

        _currentBatchNo = BatchNumberGenerator.GenerateDefaultBatchNo();

        var databasePath = Path.Combine(AppContext.BaseDirectory, "Data", "Database", "juli-mvs.db");
        _repository = new SqliteInspectionRepository(databasePath);
        _fileLogger = new FileLogger(Path.Combine(AppContext.BaseDirectory, "Data", "Logs"));
        _inspectionFileStore = new InspectionFileStore(AppContext.BaseDirectory);
        _inspectionReportWriter = new InspectionReportWriter(AppContext.BaseDirectory, LocalJsonOptions);
        _changeoverTemplateReportWriter = new ChangeoverTemplateReportWriter(AppContext.BaseDirectory, LocalJsonOptions);
        _templateImagePathResolver = new TemplateImagePathResolver(AppContext.BaseDirectory);
        _inspectionRunCoordinator = new InspectionRunCoordinator(
            _visionService,
            _repository,
            _inspectionFileStore,
            _inspectionReportWriter);
        _plcPollingCoordinator = new PlcPollingCoordinator(_plcTriggerGate);
        _machineSettingsStore = new MachineSettingsStore(AppContext.BaseDirectory, LocalJsonOptions);
        _localAppSettingsStore = new LocalAppSettingsStore(AppContext.BaseDirectory, LocalJsonOptions);
        _calibrationFileStore = new CalibrationFileStore(AppContext.BaseDirectory, LocalJsonOptions);
        _reportSaveService = new ReportSaveService(
            _inspectionReportWriter,
            _calibrationFileStore,
            new CalibrationReportThresholds(
                MaximumAcceptedLensDistortionRmsPixels,
                MaximumAcceptedCameraCalibrationRmsMm,
                MinimumAcceptedRAxisCenterPointCount,
                MinimumAcceptedRAxisCenterAngleCoverageDegrees,
                MaximumAcceptedRAxisCenterRmsMm,
                MaximumAcceptedRAxisCenterMaxMm));
    }

    private enum UserRole
    {
        Operator,
        Technician
    }

    private sealed record CameraCaptureResult(
        CameraFrame Frame,
        Mat Image,
        string? ImagePath,
        string? MetadataPath);

}

