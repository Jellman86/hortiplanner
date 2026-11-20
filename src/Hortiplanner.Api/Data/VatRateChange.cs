namespace Hortiplanner.Api.Data;

/// <summary>
/// A VAT rate from a day on. Kept for good, never changed: a new rate is a new row, so work written up for an earlier
/// day gets the rate of that day, and the owner can see who changed a rate and when.
/// </summary>
internal sealed class VatRateChange
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Standard or Reduced: the zero rate is always 0, so it has no rows.</summary>
    public required VatBand Band { get; init; }

    /// <summary>The rate as a percentage, such as 20 or 17.5.</summary>
    public required decimal Percent { get; init; }

    /// <summary>The day it starts.</summary>
    public required DateOnly From { get; init; }

    /// <summary>Who set it; nobody for the starting rates.</summary>
    public Guid? SetBy { get; init; }

    public DateTimeOffset SetAt { get; init; }
}
