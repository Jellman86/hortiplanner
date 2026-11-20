using Hortiplanner.Api.Data;
using Hortiplanner.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Costs;

/// <summary>
/// A batch's costing, worked out afresh on every read so a voided purchase or a corrected cost is always reflected.
/// Every amount is a pair, with VAT and before VAT. Which of the two counts follows whether the nursery is
/// registered for VAT as it stands now.
/// </summary>
internal static class BatchCostings
{
    /// <summary>The entries nothing has replaced.</summary>
    internal static IQueryable<BatchCost> Standing(IQueryable<BatchCost> costs) =>
        costs.Where(cost => !costs.Any(other => other.ReplacesId == cost.Id));

    /// <summary>
    /// Batches nobody has said the cost of yet, so a tray can ask. Cuttings taken here cost nothing to buy, and a
    /// batch from a purchase takes its cost from there.
    /// </summary>
    internal static async Task<HashSet<Guid>> MissingAsync(HortiplannerDbContext db, IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken)
    {
        if (batchIds.Count == 0 || !await db.IsOnAsync(NurseryFeature.Costs, cancellationToken))
        {
            return [];
        }

        var answered = Standing(db.BatchCosts).Where(cost => cost.Kind == CostKind.Material && cost.Answer != CostAnswer.Removed);

        return (await db.Batches.AsNoTracking()
            .Where(batch => batchIds.Contains(batch.Id) && batch.Source != BatchSource.Cuttings)
            .Where(batch => batch.PurchaseLineId == null
                || !db.PurchaseLines.Any(line => line.Id == batch.PurchaseLineId && db.Purchases.Any(purchase => purchase.Id == line.PurchaseId && purchase.VoidedAt == null)))
            .Where(batch => !answered.Any(cost => cost.BatchId == batch.Id))
            .Select(batch => batch.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
    }

    internal static async Task<IResult> GetAsync(Guid id, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await db.Batches.AsNoTracking()
                .Where(batch => batch.Id == id)
                .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new { batch.TraceabilityCode, batch.Source, batch.PurchaseLineId, plant.VatBand })
                .FirstOrDefaultAsync(cancellationToken) is not { } batch)
        {
            return TypedResults.NotFound();
        }

        var registered = await db.NurserySettings.AsNoTracking().Select(settings => settings.VatRegistered).FirstOrDefaultAsync(cancellationToken);
        var history = await HistoryAsync(id, db, cancellationToken);
        var purchase = batch.PurchaseLineId is { } lineId ? await PurchaseShareAsync(lineId, id, db, cancellationToken) : null;
        var material = Material(batch.Source, history, purchase);
        var others = history.Where(entry => entry is { Kind: CostKind.Other, Replaced: false, Answer: CostAnswer.Amount }).ToList();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone).DateTime);

        return TypedResults.Ok(new BatchCosting(
            id,
            batch.TraceabilityCode,
            batch.Source,
            batch.VatBand,
            registered,
            registered ? VatCounts.BeforeVat : VatCounts.WithVat,
            material,
            others,
            Total(material, others),
            history,
            await VatRates.PercentAsync(db, VatBand.Standard, today, cancellationToken),
            await VatRates.PercentAsync(db, VatBand.Reduced, today, cancellationToken)));
    }

    /// <summary>Every entry, replaced ones too, oldest first.</summary>
    private static async Task<List<CostEntry>> HistoryAsync(Guid batchId, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var costs = db.BatchCosts.AsNoTracking().Where(cost => cost.BatchId == batchId);

        return await costs
            .OrderBy(cost => cost.RecordedAt)
            .ThenBy(cost => cost.Id)
            .Select(cost => new CostEntry(
                cost.Id,
                cost.Kind,
                cost.Answer,
                cost.AmountPaid,
                cost.IncludesVat,
                cost.VatBand,
                cost.VatRate,
                cost.VatSaid,
                cost.NetCost == null ? null : new MoneyPair(cost.NetCost.Value + cost.Vat!.Value, cost.NetCost.Value),
                cost.Note,
                db.Users.Where(user => user.Id == cost.RecordedBy).Select(user => user.UserName).FirstOrDefault() ?? "Someone",
                cost.OccurredAt,
                cost.RecordedAt,
                costs.Any(other => other.ReplacesId == cost.Id),
                cost.ReplacesId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The batch's part of the purchase line it arrived on, when that purchase isn't voided: shared between the
    /// batches started from the line by how many each started with, or equally when any was started uncounted.
    /// </summary>
    private static async Task<PurchaseSource?> PurchaseShareAsync(Guid lineId, Guid batchId, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var line = await db.PurchaseLines.AsNoTracking()
            .Where(line => line.Id == lineId)
            .Join(db.Purchases.Where(purchase => purchase.VoidedAt == null), line => line.PurchaseId, purchase => purchase.Id, (line, purchase) => new
            {
                line.NetCost,
                line.Vat,
                purchase.Id,
                purchase.PurchasedOn,
                Supplier = db.Suppliers.Where(supplier => supplier.Id == purchase.SupplierId).Select(supplier => supplier.Name).First(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (line is null)
        {
            return null;
        }

        var started = await db.Batches.AsNoTracking()
            .Where(batch => batch.PurchaseLineId == lineId)
            .Select(batch => new
            {
                batch.Id,
                Quantity = db.EventLines
                    .Where(eventLine => eventLine.BatchId == batch.Id && eventLine.QuantityChange > 0)
                    .Join(db.Events, eventLine => eventLine.EventId, e => e.Id, (eventLine, e) => new { eventLine.QuantityChange, e.OccurredAt, e.Type })
                    .Where(row => row.Type == EventType.Received || row.Type == EventType.Sown)
                    .Sum(row => (int?)row.QuantityChange) ?? 0,
            })
            .ToListAsync(cancellationToken);

        var equally = started.Exists(batch => batch.Quantity == 0);
        var mine = equally ? 1 : started.Single(batch => batch.Id == batchId).Quantity;
        var all = equally ? started.Count : started.Sum(batch => batch.Quantity);
        var net = Math.Round(line.NetCost * mine / all, 2, MidpointRounding.AwayFromZero);
        var vat = Math.Round(line.Vat * mine / all, 2, MidpointRounding.AwayFromZero);

        return new PurchaseSource(line.Id, line.Supplier, line.PurchasedOn, started.Count > 1, new MoneyPair(net + vat, net));
    }

    /// <summary>The seed or plants: a purchase first, then what someone said, then cuttings costing nothing.</summary>
    private static MaterialCost Material(BatchSource source, List<CostEntry> history, PurchaseSource? purchase)
    {
        var said = history.LastOrDefault(entry => entry is { Kind: CostKind.Material, Replaced: false });

        if (purchase is not null)
        {
            return new MaterialCost(MaterialState.Purchase, purchase.Share, null, purchase, said is { Answer: CostAnswer.Amount or CostAnswer.Free } ? said : null);
        }

        return said?.Answer switch
        {
            CostAnswer.Amount => new MaterialCost(MaterialState.Amount, said.Cost, said, null, null),
            CostAnswer.Free => new MaterialCost(MaterialState.Free, new MoneyPair(0, 0), said, null, null),
            CostAnswer.NotKnown => new MaterialCost(MaterialState.NotKnown, null, said, null, null),
            _ when source == BatchSource.Cuttings => new MaterialCost(MaterialState.Cuttings, new MoneyPair(0, 0), null, null, null),
            _ => new MaterialCost(MaterialState.NoAnswer, null, null, null, null),
        };
    }

    /// <summary>Only when the seed or plants are known, as a cost, nothing, or cuttings.</summary>
    private static MoneyPair? Total(MaterialCost material, List<CostEntry> others) =>
        material.Cost is { } cost
            ? new MoneyPair(cost.WithVat + others.Sum(entry => entry.Cost!.WithVat), cost.BeforeVat + others.Sum(entry => entry.Cost!.BeforeVat))
            : null;

    internal sealed record MoneyPair(decimal WithVat, decimal BeforeVat);

    /// <param name="AddedAt">When it reached Hortiplanner.</param>
    /// <param name="OccurredAt">The day it was for, which gives the VAT rate.</param>
    internal sealed record CostEntry(
        Guid Id,
        CostKind Kind,
        CostAnswer Answer,
        decimal? AmountPaid,
        bool? IncludesVat,
        VatBand? VatBand,
        decimal? VatRate,
        bool VatSaid,
        MoneyPair? Cost,
        string? Note,
        string AddedBy,
        DateTimeOffset OccurredAt,
        DateTimeOffset AddedAt,
        bool Replaced,
        Guid? ReplacesId);

    /// <param name="Shared">Whether other batches were started from the same line, so this is a share of it.</param>
    internal sealed record PurchaseSource(Guid PurchaseId, string Supplier, DateOnly PurchasedOn, bool Shared, MoneyPair Share);

    /// <param name="Superseded">A cost typed in before the purchase gave the batch its cost, which is no longer counted.</param>
    internal sealed record MaterialCost(MaterialState State, MoneyPair? Cost, CostEntry? Entry, PurchaseSource? Purchase, CostEntry? Superseded);

    /// <param name="StandardRate">Today's standard rate, to name in the choice of VAT.</param>
    internal sealed record BatchCosting(
        Guid BatchId,
        string TraceabilityCode,
        BatchSource Source,
        VatBand PlantVatBand,
        bool VatRegistered,
        VatCounts Counts,
        MaterialCost Material,
        IReadOnlyList<CostEntry> Others,
        MoneyPair? Total,
        IReadOnlyList<CostEntry> History,
        decimal StandardRate,
        decimal ReducedRate);
}
