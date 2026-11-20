namespace Hortiplanner.Api.Data;

/// <summary>Which VAT rate applies: the standard or reduced rate as set under Settings, or the zero rate, always 0%.</summary>
internal enum VatBand
{
    Standard,
    Reduced,
    Zero,
}
