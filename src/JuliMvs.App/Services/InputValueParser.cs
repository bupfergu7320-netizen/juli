using System.Globalization;

namespace JuliMvs.App.Services;

internal static class InputValueParser
{
    public static double ReadRequiredDouble(string value, string name, double min, double max)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException($"{name}\u5fc5\u987b\u662f\u6570\u5b57\u3002");
        }

        if (parsed < min || parsed > max)
        {
            throw new InvalidOperationException(
                $"{name}\u5fc5\u987b\u5728{min.ToString(CultureInfo.InvariantCulture)}\u5230{max.ToString(CultureInfo.InvariantCulture)}\u4e4b\u95f4\u3002");
        }

        return parsed;
    }

    public static int ReadRequiredInt(string value, string name, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException($"{name}\u5fc5\u987b\u662f\u6574\u6570\u3002");
        }

        if (parsed < min || parsed > max)
        {
            throw new InvalidOperationException(
                $"{name}\u5fc5\u987b\u5728{min.ToString(CultureInfo.InvariantCulture)}\u5230{max.ToString(CultureInfo.InvariantCulture)}\u4e4b\u95f4\u3002");
        }

        return parsed;
    }

    public static double ReadMachineTransformNumber(string value, string name)
    {
        var trimmed = value.Trim();
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"{name}\u5fc5\u987b\u662f\u6570\u5b57\u3002");
    }

    public static string FormatMachineTransformNumber(double value)
    {
        return Math.Abs(value) < 0.0000005
            ? "0"
            : value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    public static string FormatBool(bool value)
    {
        return value ? "\u662f" : "\u5426";
    }
}
