using Hortiplanner.Api.Data;
using Hortiplanner.Api.Figures;
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
            // A batch whose start was undone was a mistake, with nothing to cost: it has no work left standing.
            .Where(batch => db.EventLines.Any(line => line.BatchId == batch.Id && !db.Events.Any(correction => correction.CorrectsEventId == line.EventId)))
            .Select(batch => batch.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
    }

    internal static async Task<IResult> GetAsync(Guid id, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken) =>
        await CostingAsync(id, db, clock, cancellationToken) is { } costing ? TypedResults.Ok(costing) : TypedResults.NotFound();

    /// <summary>A batch's whole costing, as Costs and takings shows it, or null when there's no such batch.</summary>
    internal static async Task<BatchCosting?> CostingAsync(Guid id, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken) =>
        (await LoadAsync([id], db, clock, cancellationToken)).Batches.GetValueOrDefault(id);

    internal static async Task<Costings> LoadAsync(
        IReadOnlyCollection<Guid> batchIds, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken,
        (DateOnly From, DateOnly To)? unbatchedWindow = null)
    {
        var registered = await db.NurserySettings.AsNoTracking().Select(settings => settings.VatRegistered).FirstOrDefaultAsync(cancellationToken);
        var batches = await db.Batches.AsNoTracking()
            .Where(batch => batchIds.Contains(batch.Id))
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new
            {
                batch.Id,
                batch.TraceabilityCode,
                batch.Source,
                batch.PurchaseLineId,
                plant.VatBand,
            })
            .ToListAsync(cancellationToken);

        var sales = (await Sales.ForBatchesAsync(batchIds, db, unbatchedWindow, cancellationToken)).ToLookup(sale => sale.BatchId);
        if (batches.Count == 0)
        {
            return new Costings(registered, [], sales[null]);
        }

        var history = await HistoryAsync(batchIds, db, cancellationToken);
        var purchases = await PurchaseSharesAsync(batches.Select(batch => batch.PurchaseLineId).OfType<Guid>().Distinct().ToArray(), db, cancellationToken);
        var ledger = await BatchLedger.LoadAsync(db, batchIds, cancellationToken);
        var potting = await PotAllowances.ForBatchesAsync(ledger, db, clock, cancellationToken);
        var rows = ledger.Rows.ToLookup(row => row.Line.BatchId);
        var absorbedAt = ledger.AbsorbedAt.ToLookup(item => item.BatchId);
        var counted = BatchLedger.CountedBatches(ledger).ToHashSet();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var (standard, reduced) = await VatRates.PairAsync(db, today, cancellationToken);

        var costings = new Dictionary<Guid, BatchCosting>();
        foreach (var batch in batches)
        {
            var entries = history[batch.Id].ToList();
            var material = Material(batch.Source, entries, purchases.GetValueOrDefault(batch.Id));
            var others = entries.Where(entry => entry is { Kind: CostKind.Other, Replaced: false, Answer: CostAnswer.Amount }).ToList();
            var (pots, charges) = potting.TryGetValue(batch.Id, out var potCost) ? potCost : ((PotAllowances.PotsCost?)null, []);
            var total = Total(material, pots, others);
            var uncounted = ledger.Batches[batch.Id].SownAs is not (null or SownMeasure.Counted) && !counted.Contains(batch.Id);
            var perPlant = PlantCosts.Work(rows[batch.Id].ToList(), ledger.Absorbed.GetValueOrDefault(batch.Id),
                absorbedAt[batch.Id], uncounted, total, Spent(material, others, charges));

            costings.Add(batch.Id, new BatchCosting(
                batch.Id, batch.TraceabilityCode, batch.Source, batch.VatBand,
                registered, registered ? VatCounts.BeforeVat : VatCounts.WithVat,
                material, others, pots, total, perPlant, TakingsOf(sales[batch.Id]), entries, standard, reduced));
        }

        return new Costings(registered, costings, sales[null]);
    }

    internal sealed record Costings(bool VatRegistered, Dictionary<Guid, BatchCosting> Batches, IEnumerable<Sales.Sale> UnbatchedSales);

    /// <summary>Every entry for the selected batches, replaced ones too, oldest first.</summary>
    private static async Task<ILookup<Guid, CostEntry>> HistoryAsync(IReadOnlyCollection<Guid> batchIds, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var costs = db.BatchCosts.AsNoTracking();
        var rows = await costs
            .Where(cost => batchIds.Contains(cost.BatchId))
            .OrderBy(cost => cost.RecordedAt)
            .ThenBy(cost => cost.Id)
            .Select(cost => new
            {
                cost.BatchId,
                Entry = new CostEntry(
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
                    cost.ReplacesId),
            })
            .ToListAsync(cancellationToken);
        return rows.ToLookup(row => row.BatchId, row => row.Entry);
    }

    /// <summary>Splits net and VAT between all standing batches on each purchase line, including batches outside the report.</summary>
    private static async Task<Dictionary<Guid, PurchaseSource>> PurchaseSharesAsync(Guid[] lineIds, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (lineIds.Length == 0)
        {
            return [];
        }

        var lines = await db.PurchaseLines.AsNoTracking().Where(line => lineIds.Contains(line.Id))
            .Join(db.Purchases.Where(purchase => purchase.VoidedAt == null), line => line.PurchaseId, purchase => purchase.Id, (line, purchase) => new
            {
                LineId = line.Id,
                PurchaseId = purchase.Id,
                line.NetCost,
                line.Vat,
                purchase.PurchasedOn,
                Supplier = db.Suppliers.Where(supplier => supplier.Id == purchase.SupplierId).Select(supplier => supplier.Name).First(),
            })
            .ToListAsync(cancellationToken);

        var starts = db.Events.AsNoTracking().Where(e => e.Type == EventType.Sown || e.Type == EventType.Received);
        var standing = db.Events.AsNoTracking()
            .Where(e => !db.Events.Any(undo => undo.CorrectsEventId == e.Id)
                && (e.Type == EventType.Sown || e.Type == EventType.Received
                    || (e.Type == EventType.Corrected && starts.Any(start => start.Id == e.CorrectsEventId))));
        var counts = await db.Batches.AsNoTracking()
            .Where(batch => batch.PurchaseLineId != null && lineIds.Contains(batch.PurchaseLineId.Value))
            .Join(db.EventLines, batch => batch.Id, line => line.BatchId, (batch, line) => new { LineId = batch.PurchaseLineId!.Value, line.BatchId, line.EventId, line.QuantityChange })
            .Where(line => standing.Any(e => e.Id == line.EventId))
            .GroupBy(line => new { line.LineId, line.BatchId })
            .Select(group => new { group.Key.LineId, group.Key.BatchId, Count = group.Sum(line => line.QuantityChange) })
            .ToListAsync(cancellationToken);
        var byLine = counts.ToLookup(row => row.LineId);
        var shares = new Dictionary<Guid, PurchaseSource>();

        foreach (var line in lines)
        {
            var started = byLine[line.LineId].OrderBy(row => row.BatchId).ToArray();
            if (started.Length == 0)
            {
                continue;
            }

            var equally = started.Any(row => row.Count <= 0);
            var weights = started.Select(row => equally ? 1 : row.Count).ToArray();
            var weight = weights.Sum(value => (long)value);
            var net = PennyShares.Allocate(line.NetCost, weights.Select(value => line.NetCost * 100m * value / weight));
            var vat = PennyShares.Allocate(line.Vat, weights.Select(value => line.Vat * 100m * value / weight));

            for (var index = 0; index < started.Length; index++)
            {
                shares.Add(started[index].BatchId, new PurchaseSource(line.PurchaseId, line.Supplier, line.PurchasedOn,
                    started.Length > 1, equally, new MoneyPair(net[index] + vat[index], net[index])));
            }
        }

        return shares;
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

    /// <summary>What the batch has taken: on invoices, and from trays with and without a price.</summary>
    private static Takings TakingsOf(IEnumerable<Sales.Sale> sales)
    {
        MoneyPair Sum(IEnumerable<Sales.Sale> some) =>
            new(some.Sum(sale => sale.Takings?.WithVat ?? 0), some.Sum(sale => sale.Takings?.BeforeVat ?? 0));

        var invoiced = sales.Where(sale => sale.Source == SaleSource.Invoice).ToList();
        var priced = sales.Where(sale => sale.Source == SaleSource.Tray && sale.Takings is not null).ToList();

        return new Takings(
            invoiced.Sum(sale => sale.Quantity),
            Sum(invoiced),
            priced.Sum(sale => sale.Quantity),
            Sum(priced),
            sales.Where(sale => sale.Source == SaleSource.Tray && sale.Takings is null).Sum(sale => sale.Quantity),
            Sum(sales));
    }

    /// <summary>
    /// What was spent on the batch and when: the seed or plants from the start, anything else on the day it was for,
    /// and each potting as it happened.
    /// </summary>
    private static List<PlantCosts.Charge> Spent(MaterialCost material, List<CostEntry> others, List<PlantCosts.Charge> potting) =>
        [
            .. material.Cost is { } cost ? [new PlantCosts.Charge(DateTimeOffset.MinValue, cost)] : Array.Empty<PlantCosts.Charge>(),
            .. others.Select(entry => new PlantCosts.Charge(entry.OccurredAt, entry.Cost!)),
            .. potting,
        ];

    /// <summary>Only when the seed or plants are known, as a cost, nothing, or cuttings; worked out exactly, rounded only for showing.</summary>
    private static MoneyPair? Total(MaterialCost material, PotAllowances.PotsCost? pots, List<CostEntry> others) =>
        material.Cost is { } cost
            ? new MoneyPair(
                cost.WithVat + (pots?.Cost.WithVat ?? 0) + others.Sum(entry => entry.Cost!.WithVat),
                cost.BeforeVat + (pots?.Cost.BeforeVat ?? 0) + others.Sum(entry => entry.Cost!.BeforeVat))
            : null;

    internal sealed record MoneyPair(decimal WithVat, decimal BeforeVat);

    /// <param name="Invoiced">Plants sold on invoices not cancelled by a credit note.</param>
    /// <param name="FromTrays">Plants recorded as sold from a tray, with a price.</param>
    /// <param name="NoPrice">Plants recorded as sold from a tray with no price, which add nothing to the takings.</param>
    internal sealed record Takings(int Invoiced, MoneyPair InvoiceTakings, int FromTrays, MoneyPair TrayTakings, int NoPrice, MoneyPair Total)
    {
        public int Sold => Invoiced + FromTrays + NoPrice;
    }

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
    /// <param name="Equally">Shared equally, because a batch from the line was sown without a count.</param>
    internal sealed record PurchaseSource(Guid PurchaseId, string Supplier, DateOnly PurchasedOn, bool Shared, bool Equally, MoneyPair Share);

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
        PotAllowances.PotsCost? Pots,
        MoneyPair? Total,
        PlantCosts.PerPlant PerPlant,
        Takings Takings,
        IReadOnlyList<CostEntry> History,
        decimal StandardRate,
        decimal ReducedRate);
}
