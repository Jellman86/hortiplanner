using System.Globalization;
using Hortiplanner.Api.Data;

namespace Hortiplanner.Api.Transfer;

/// <summary>One spreadsheet row's values, read by field, noting each problem in words as it goes.</summary>
internal sealed class RowReader(IReadOnlyDictionary<string, string?> values, ImportKind kind)
{
    private static readonly string[] Yes = ["yes", "y", "true", "1", "x"];

    private static readonly string[] No = ["no", "n", "false", "0"];

    /// <summary>The words a spreadsheet might use for each VAT rate. The plants sheet writes the first of each.</summary>
    private static readonly Dictionary<string, VatBand> VatWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["standard"] = VatBand.Standard,
        ["standard rate"] = VatBand.Standard,
        ["s"] = VatBand.Standard,
        ["reduced"] = VatBand.Reduced,
        ["reduced rate"] = VatBand.Reduced,
        ["r"] = VatBand.Reduced,
        ["zero"] = VatBand.Zero,
        ["zero rate"] = VatBand.Zero,
        ["zero rated"] = VatBand.Zero,
        ["z"] = VatBand.Zero,
        ["none"] = VatBand.Zero,
        ["no vat"] = VatBand.Zero,
        ["0"] = VatBand.Zero,
        ["0%"] = VatBand.Zero,
    };

    public List<string> Problems { get; } = [];

    public string? Text(string key)
    {
        var field = ImportFields.Find(kind, key);
        var value = values.GetValueOrDefault(key)?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Length > field.Longest)
        {
            Problems.Add($"{field.Label} is longer than {field.Longest} letters.");
            return null;
        }

        return value;
    }

    public string? Required(string key)
    {
        var value = Text(key);

        if (value is null && Problems.Count == 0)
        {
            Problems.Add($"{ImportFields.Find(kind, key).Label} is empty.");
        }

        return value;
    }

    public bool? YesNo(string key)
    {
        var value = values.GetValueOrDefault(key)?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (Yes.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (No.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        Problems.Add($"{ImportFields.Find(kind, key).Label} should be yes or no, not \"{value}\".");
        return null;
    }

    public int? Number(string key, int least, int most)
    {
        var value = values.GetValueOrDefault(key)?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= least && number <= most)
        {
            return number;
        }

        Problems.Add($"{ImportFields.Find(kind, key).Label} should be a whole number from {least} to {most}, not \"{value}\".");
        return null;
    }

    public VatBand? VatRate(string key)
    {
        var value = values.GetValueOrDefault(key)?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (VatWords.TryGetValue(value, out var band))
        {
            return band;
        }

        Problems.Add($"{ImportFields.Find(kind, key).Label} should be standard, reduced or zero, not \"{value}\".");
        return null;
    }
}
