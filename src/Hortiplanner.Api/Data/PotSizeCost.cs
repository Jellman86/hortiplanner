namespace Hortiplanner.Api.Data;

/// <summary>
/// What it costs to pot one plant into a size, from a day: the pot, the compost that fills it and the label. Never
/// changed: a new amount is a new row, so potting already recorded keeps the amount it had.
/// </summary>
internal sealed class PotSizeCost
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid PotSizeId { get; init; }

    /// <summary>The amount as typed. Null takes the allowance away from this day.</summary>
    public decimal? CostEach { get; init; }

    /// <summary>Whether the amount has VAT in it, as it does unless the owner says it's before VAT.</summary>
    public required bool IncludesVat { get; init; }

    /// <summary>The standard rate on <see cref="From"/>: pots, compost and labels are standard rate.</summary>
    public required decimal VatRate { get; init; }

    /// <summary>The day it applies from, which is the day it was set.</summary>
    public required DateOnly From { get; init; }

    public required Guid SetBy { get; init; }

    public DateTimeOffset SetAt { get; private set; }
}
