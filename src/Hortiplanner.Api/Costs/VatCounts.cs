namespace Hortiplanner.Api.Costs;

/// <summary>Which figures count: with VAT while the nursery isn't registered, since it can't claim the VAT back.</summary>
internal enum VatCounts
{
    WithVat,
    BeforeVat,
}
