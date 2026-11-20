namespace Hortiplanner.Api.Data;

/// <summary>
/// What a batch cost, as someone said it. Never changed or removed: a correction is a new entry that replaces the
/// one it corrects, so every answer is kept with who gave it and when.
/// </summary>
internal sealed class BatchCost
{
    /// <summary>Made on the device, so a resend from an offline phone is recognised.</summary>
    public required Guid Id { get; init; }

    public required Guid BatchId { get; init; }

    public required CostKind Kind { get; init; }

    public required CostAnswer Answer { get; init; }

    /// <summary>What was paid, as typed. Only for <see cref="CostAnswer.Amount"/>.</summary>
    public decimal? AmountPaid { get; init; }

    /// <summary>Whether the typed amount has VAT in it.</summary>
    public bool? IncludesVat { get; init; }

    public VatBand? VatBand { get; init; }

    /// <summary>The percentage for <see cref="VatBand"/> on the day of the cost, kept so a later rate change never reaches back.</summary>
    public decimal? VatRate { get; init; }

    /// <summary>Whether the owner said what VAT was in it, rather than Hortiplanner assuming it.</summary>
    public bool VatSaid { get; init; }

    /// <summary>The cost before VAT, worked out once to the penny when the entry was stored.</summary>
    public decimal? NetCost { get; init; }

    public decimal? Vat { get; init; }

    /// <summary>What it was, for a cost other than the seed or plants. No personal data.</summary>
    public string? Note { get; init; }

    /// <summary>The entry this one corrects.</summary>
    public Guid? ReplacesId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset RecordedAt { get; private set; }

    public required Guid RecordedBy { get; init; }

    public string? RequestFingerprint { get; init; }
}
