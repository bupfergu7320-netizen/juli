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
    private async Task<string?> CaptureCameraImageAsync(
        bool saveImage,
        CalibrationImageSaveTarget? saveTarget = null)
    {
        if (!_cameraConnected)
        {
            throw new InvalidOperationException("相机未连接。");
        }

        if (!await _captureLock.WaitAsync(TimeSpan.FromMilliseconds(CameraCaptureTimeoutMilliseconds)))
        {
            throw new InvalidOperationException("相机正在拍照，请等待本次拍照完成后再操作。");
        }

        try
        {
            if (_cameraSettings.CaptureDelaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(_cameraSettings.CaptureDelaySeconds));
            }

            var captureContext = saveImage
                ? _calibrationFileStore.CreateCameraCaptureContext(
                    saveTarget,
                    _connectedCameraInfo,
                    _batchSession,
                    _cameraSettings,
                    ReadVisionParameters())
                : null;
            var capture = await Task.Run(async () =>
            {
                var capturedFrame = await _cameraService.CaptureAsync(timeoutMilliseconds: CameraCaptureTimeoutMilliseconds);
                using var capturedImage = ConvertCameraFrameToMat(capturedFrame);

                if (captureContext is not null)
                {
                    Directory.CreateDirectory(captureContext.Directory);
                    Cv2.ImWrite(captureContext.ImagePath, capturedImage);
                    _calibrationFileStore.SaveCameraMetadata(captureContext, capturedFrame);
                }

                return new CameraCaptureResult(
                    capturedFrame,
                    capturedImage.Clone(),
                    captureContext?.ImagePath,
                    captureContext?.MetadataPath);
            });

            _lastCameraImage?.Dispose();
            _lastCameraImage = capture.Image;

            Log(saveImage
                ? $"相机拍照完成: {capture.Frame.Width}x{capture.Frame.Height}, {capture.Frame.PixelFormat}, {capture.ImagePath}"
                : $"相机拍照完成: {capture.Frame.Width}x{capture.Frame.Height}, {capture.Frame.PixelFormat}, 生产模式不保存图片");
            if (capture.MetadataPath is not null)
            {
                Log($"相机元数据已保存: {capture.MetadataPath}");
            }

            return capture.ImagePath;
        }
        finally
        {
            _captureLock.Release();
        }
    }

    private static Mat ConvertCameraFrameToMat(CameraFrame frame)
    {
        var format = frame.PixelFormat;
        if (format.Contains("Mono8", StringComparison.OrdinalIgnoreCase))
        {
            return CreateMatFromBuffer(frame, MatType.CV_8UC1);
        }

        if (format.Contains("Mono10", StringComparison.OrdinalIgnoreCase) ||
            format.Contains("Mono12", StringComparison.OrdinalIgnoreCase) ||
            format.Contains("Mono16", StringComparison.OrdinalIgnoreCase))
        {
            using var mono16 = CreateMatFromBuffer(frame, MatType.CV_16UC1);
            var mono8 = new Mat();
            Cv2.Normalize(mono16, mono8, 0, 255, NormTypes.MinMax, MatType.CV_8UC1.ToInt32());
            return mono8;
        }

        if (format.Contains("BGR8", StringComparison.OrdinalIgnoreCase))
        {
            return CreateMatFromBuffer(frame, MatType.CV_8UC3);
        }

        if (format.Contains("RGB8", StringComparison.OrdinalIgnoreCase))
        {
            using var rgb = CreateMatFromBuffer(frame, MatType.CV_8UC3);
            var bgr = new Mat();
            Cv2.CvtColor(rgb, bgr, ColorConversionCodes.RGB2BGR);
            return bgr;
        }

        if (format.Contains("BayerRG8", StringComparison.OrdinalIgnoreCase))
        {
            using var bayer = CreateMatFromBuffer(frame, MatType.CV_8UC1);
            var bgr = new Mat();
            Cv2.CvtColor(bayer, bgr, ColorConversionCodes.BayerRG2BGR);
            return bgr;
        }

        if (format.Contains("BayerBG8", StringComparison.OrdinalIgnoreCase))
        {
            using var bayer = CreateMatFromBuffer(frame, MatType.CV_8UC1);
            var bgr = new Mat();
            Cv2.CvtColor(bayer, bgr, ColorConversionCodes.BayerBG2BGR);
            return bgr;
        }

        if (format.Contains("BayerGR8", StringComparison.OrdinalIgnoreCase))
        {
            using var bayer = CreateMatFromBuffer(frame, MatType.CV_8UC1);
            var bgr = new Mat();
            Cv2.CvtColor(bayer, bgr, ColorConversionCodes.BayerGR2BGR);
            return bgr;
        }

        if (format.Contains("BayerGB8", StringComparison.OrdinalIgnoreCase))
        {
            using var bayer = CreateMatFromBuffer(frame, MatType.CV_8UC1);
            var bgr = new Mat();
            Cv2.CvtColor(bayer, bgr, ColorConversionCodes.BayerGB2BGR);
            return bgr;
        }

        throw new NotSupportedException($"暂不支持相机像素格式: {format}。请在MVS里把像素格式改为Mono8、BGR8、RGB8或Bayer8。");
    }

    private static Mat CreateMatFromBuffer(CameraFrame frame, MatType type)
    {
        var mat = new Mat(frame.Height, frame.Width, type);
        var expectedLength = checked((int)(mat.Total() * mat.ElemSize()));
        if (frame.Buffer.Length < expectedLength)
        {
            mat.Dispose();
            throw new InvalidOperationException(
                $"相机图像数据长度不足。格式={frame.PixelFormat}, 图像={frame.Width}x{frame.Height}, 实际={frame.Buffer.Length}, 需要={expectedLength}");
        }

        Marshal.Copy(frame.Buffer, 0, mat.Data, expectedLength);
        return mat;
    }
}
