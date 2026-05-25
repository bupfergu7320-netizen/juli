using System.Globalization;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;
using JuliMvs.Plc;
using JuliMvs.Vision;

namespace JuliMvs.App.Services;

internal sealed class InspectionDiagnosticMessageFormatter
{
    public string BuildTurntableResultDetails(
        InspectionResult result,
        PartTemplate? template,
        PlcOutputTransform outputTransform,
        XyrAlignmentSnapshot? alignmentSnapshot = null,
        IReadOnlyList<ContourCandidateDiagnostic>? candidateDiagnostics = null,
        AngleResolutionDiagnostic? angleDiagnostic = null)
    {
        var lines = new List<string>
        {
            result.Message
        };

        if (result.Measurement is not { } measurement)
        {
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add(Format(
            "\u5f53\u524d\u5c3a\u5bf8: W={0:F3}mm H={1:F3}mm Area={2:F0}px",
            measurement.WidthMm,
            measurement.HeightMm,
            measurement.AreaPixels));

        if (template is not null)
        {
            var widthDiff = Math.Abs(measurement.WidthMm - template.WidthMm);
            var heightDiff = Math.Abs(measurement.HeightMm - template.HeightMm);
            var areaDiffPercent = Math.Abs(measurement.AreaPixels - template.AreaPixels) /
                Math.Max(template.AreaPixels, 0.0001) * 100.0;

            lines.Add(Format(
                "\u76ee\u6807\u6807\u51c6\u4f4d: X={0:F3}mm Y={1:F3}mm R={2:F3}deg",
                template.ReferenceCenterXMm,
                template.ReferenceCenterYMm,
                template.ReferenceAngleDegrees));
            lines.Add(Format(
                "\u6a21\u677f\u5c3a\u5bf8: W={0:F3}mm H={1:F3}mm Area={2:F0}px",
                template.WidthMm,
                template.HeightMm,
                template.AreaPixels));
            lines.Add(Format(
                "\u5c3a\u5bf8\u5dee\u503c: dW={0:F3}mm dH={1:F3}mm dArea={2:F2}%",
                widthDiff,
                heightDiff,
                areaDiffPercent));
        }

        if (alignmentSnapshot is not null)
        {
            lines.Add(Format(
                "\u5f53\u524d\u4f4d\u59ff: X={0:F3}mm Y={1:F3}mm R={2:F3}deg",
                alignmentSnapshot.CurrentPose.XMm,
                alignmentSnapshot.CurrentPose.YMm,
                alignmentSnapshot.CurrentPose.AngleDegrees));
            lines.Add(Format(
                "\u6807\u51c6\u4f4d\u59ff: X={0:F3}mm Y={1:F3}mm R={2:F3}deg",
                alignmentSnapshot.TemplatePose.XMm,
                alignmentSnapshot.TemplatePose.YMm,
                alignmentSnapshot.TemplatePose.AngleDegrees));
            lines.Add(alignmentSnapshot.RAxisCenterEnabled
                ? Format(
                    "R\u8f74\u4e2d\u5fc3: X={0:F3}mm Y={1:F3}mm",
                    alignmentSnapshot.RAxisCenter.XMm,
                    alignmentSnapshot.RAxisCenter.YMm)
                : "R\u8f74\u4e2d\u5fc3: \u672a\u542f\u7528");
            lines.Add(Format(
                "R\u540e\u4e2d\u5fc3: X={0:F3}mm Y={1:F3}mm",
                alignmentSnapshot.CenterAfterRotation.XMm,
                alignmentSnapshot.CenterAfterRotation.YMm));
            lines.Add(Format(
                "Home2D\u52a8\u4f5c\u91cf: X={0:F3}mm Y={1:F3}mm R={2:F3}deg",
                alignmentSnapshot.HomeXActionMm,
                alignmentSnapshot.HomeYActionMm,
                alignmentSnapshot.HomeRActionDegrees));
            lines.Add(Format(
                "R\u547d\u4ee4\u65b9\u5411: {0}; \u89c6\u89c9R\u7ea0\u504f={1:F3}deg; PLC\u5b9e\u9645R\u8f93\u51fa={2:F3}deg; \u53c2\u4e0eXY\u8ba1\u7b97\u7684\u5b9e\u9645\u65cb\u8f6c={3:F3}deg",
                alignmentSnapshot.RCommandDirection < 0 ? "\u53d6\u53cd\u5e76\u540c\u6b65\u91cd\u7b97XY" : "\u4e0d\u53d6\u53cd",
                alignmentSnapshot.VisionHomeRActionDegrees,
                alignmentSnapshot.HomeRActionDegrees,
                alignmentSnapshot.PhysicalRotationDegrees));
        }

        var plcOutput = PlcOutputDiagnosticFormatter.CalculatePlcOutputCommand(measurement, outputTransform);
        var preRotationOutput = PlcOutputDiagnosticFormatter.CalculatePreRotationCorrectionCommand(measurement, outputTransform);
        lines.Add(Format(
            "\u504f\u5dee: X={0:F3}mm Y={1:F3}mm R={2:F3}deg",
            measurement.XOffsetMm,
            measurement.YOffsetMm,
            measurement.AngleOffsetDegrees));
        lines.Add(Format(
            "\u4fee\u6b63\u504f\u5dee: X={0:F3}mm Y={1:F3}mm R={2:F3}deg",
            -measurement.XOffsetMm,
            -measurement.YOffsetMm,
            -measurement.AngleOffsetDegrees));
        lines.Add(Format(
            "PLC\u65cb\u8f6c\u524d\u7ea0\u504f\u91cf: X={0}mm Y={1}mm R={2}deg",
            PlcOutputDiagnosticFormatter.FormatPlcValueText(-measurement.XOffsetMm),
            PlcOutputDiagnosticFormatter.FormatPlcValueText(-measurement.YOffsetMm),
            PlcOutputDiagnosticFormatter.FormatPlcValueText(-measurement.AngleOffsetDegrees)));
        lines.Add(Format(
            "R\u540e\u6700\u7ec8\u7ea0\u504f\u53c2\u8003: X={0:F3}mm Y={1:F3}mm R={2:F3}deg",
            measurement.XCompensationMm,
            measurement.YCompensationMm,
            measurement.RotationCompensationDegrees));
        lines.Add(Format(
            "PLC\u6700\u7ec8\u7ea0\u504f\u8f93\u51fa: D1002={0} D1004={1} D1006={2}",
            PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.XDeviation),
            PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.YDeviation),
            PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.RDeviation)));
        lines.Add(Format(
            "\u65cb\u8f6c\u524d\u65e7\u8f93\u51fa\u4ec5\u53c2\u8003: D1002={0} D1004={1} D1006={2}",
            PlcOutputDiagnosticFormatter.FormatPlcValueText(preRotationOutput.XDeviation),
            PlcOutputDiagnosticFormatter.FormatPlcValueText(preRotationOutput.YDeviation),
            PlcOutputDiagnosticFormatter.FormatPlcValueText(preRotationOutput.RDeviation)));

        lines.Add($"PLC\u504f\u5dee\u8f93\u51fa\u5750\u6807\u7cfb: {FormatPlcOutputTransform(outputTransform)}");
        lines.Add(Format("\u8bc6\u522b\u5206\u6570: {0:F3}", measurement.MatchScore));
        lines.Add(result.Message.StartsWith("角度NG", StringComparison.Ordinal)
            ? "\u89d2\u5ea6\u6765\u6e90: \u6a21\u677f\u65cb\u8f6c\u5339\u914d\u4e0d\u53ef\u9760\uff0c\u6b63\u5f0f\u6d41\u7a0b\u4f1a\u5199D1010=2\u3002"
            : "\u89d2\u5ea6\u6765\u6e90: \u8be6\u89c1\u68c0\u6d4b\u8bca\u65ad\u56fe\u548c\u62a5\u544aAngle\u5b57\u6bb5\u3002");
        lines.Add(BuildAngleCandidatesText(angleDiagnostic));
        lines.Add(BuildCandidateDiagnosticsText(candidateDiagnostics));

        return string.Join(Environment.NewLine, lines);
    }

    public string BuildAngleCandidatesText(
        AngleResolutionDiagnostic? angleDiagnostic,
        int maxItems = 5)
    {
        if (angleDiagnostic?.Candidates is null || angleDiagnostic.Candidates.Count == 0)
        {
            return "\u89d2\u5ea6\u5019\u9009: 0";
        }

        var visibleCount = Math.Min(angleDiagnostic.Candidates.Count, maxItems);
        var lines = new List<string>
        {
            Format(
                "\u89d2\u5ea6\u5019\u9009: {0}\u4e2a\uff0c\u663e\u793a\u524d{1}\u4e2a",
                angleDiagnostic.Candidates.Count,
                visibleCount)
        };

        foreach (var candidate in angleDiagnostic.Candidates.Take(maxItems))
        {
            lines.Add(Format(
                "排名={0} {1}: 偏移={2:F3}deg, 结果角度={3:F3}deg, 分数={4:F3}",
                candidate.Rank,
                candidate.Stage,
                candidate.AngleOffsetDegrees,
                candidate.ResolvedAngleDegrees,
                candidate.Score));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public string BuildCandidateDiagnosticsText(
        IReadOnlyList<ContourCandidateDiagnostic>? candidateDiagnostics,
        int maxItems = 5)
    {
        if (candidateDiagnostics is null || candidateDiagnostics.Count == 0)
        {
            return "\u5019\u9009\u8f6e\u5ed3: 0";
        }

        var visibleCount = Math.Min(candidateDiagnostics.Count, maxItems);
        var lines = new List<string>
        {
            Format(
                "\u5019\u9009\u8f6e\u5ed3: {0}\u4e2a\uff0c\u663e\u793a\u524d{1}\u4e2a",
                candidateDiagnostics.Count,
                visibleCount)
        };

        foreach (var candidate in candidateDiagnostics.Take(maxItems))
        {
            var selected = candidate.IsSelected ? "\u9009\u4e2d" : "\u5019\u9009";
            lines.Add(Format(
                "{0} 排名={1} C{2} {3}: 分数={4:F3}, 中心=({5:F1}px,{6:F1}px), W={7:F3}mm H={8:F3}mm, 面积={9:F0}px, 填充={10:F3}, 距离={11:F1}px",
                selected,
                candidate.Rank,
                candidate.CandidateIndex,
                candidate.Source,
                candidate.Score,
                candidate.CenterXPixel,
                candidate.CenterYPixel,
                candidate.WidthMm,
                candidate.HeightMm,
                candidate.AreaPixels,
                candidate.FillRatio,
                candidate.CenterDistancePixels));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatPlcOutputTransform(PlcOutputTransform transform)
    {
        return
            $"D1002={InputValueParser.FormatMachineTransformNumber(transform.Xx)}*X+{InputValueParser.FormatMachineTransformNumber(transform.Xy)}*Y+{InputValueParser.FormatMachineTransformNumber(transform.XBias)}; " +
            $"D1004={InputValueParser.FormatMachineTransformNumber(transform.Yx)}*X+{InputValueParser.FormatMachineTransformNumber(transform.Yy)}*Y+{InputValueParser.FormatMachineTransformNumber(transform.YBias)}; " +
            $"D1006={InputValueParser.FormatMachineTransformNumber(transform.RScale)}*R+{InputValueParser.FormatMachineTransformNumber(transform.RBias)}";
    }

    public static string FormatTemplateBaselineSummary(PartTemplate template)
    {
        return
            $"当前型号标准位: X {template.ReferenceCenterXMm:F3}mm / Y {template.ReferenceCenterYMm:F3}mm / R {template.ReferenceAngleDegrees:F3}deg\n" +
            $"像素中心: X {template.ReferenceCenterXPixel:F1}px / Y {template.ReferenceCenterYPixel:F1}px";
    }

    public static string FormatSavedImagePath(string? imagePath)
    {
        return string.IsNullOrWhiteSpace(imagePath) ? "\u672a\u4fdd\u5b58" : imagePath;
    }

    private static string Format(string format, params object[] args)
    {
        return string.Format(CultureInfo.InvariantCulture, format, args);
    }

}
