using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Costs;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Settings;
using Hortiplanner.Api.Transfer;
using Hortiplanner.Api.Work;
using Microsoft.EntityFrameworkCore;
using MoneyPair = Hortiplanner.Api.Costs.BatchCostings.MoneyPair;

namespace Hortiplanner.Api.Figures;

/// <summary>
/// What the plants cost and made: each batch's costing, the same one Costs and takings shows, added up by plant,
/// by the year batches were started and by supplier. Batches nobody has answered for make their plant's figures
/// incomplete; batches said not to be known are left out of cost and profit for good. Profit is worked out on the
/// figures that count: with VAT while the nursery isn't registered, before VAT once it is.
/// </summary>
internal static class MoneyFigures
{
    public static void MapMoneyFigureEndpoints(this IEndpointRouteBuilder app)
    {
        var money = app.MapGroup("/api/figures").RequireAuthorization(AccountServices.OwnerPolicy).RequireFeature(NurseryFeature.Costs);
        money.MapGet("/money", GetAsync).Produces<Money>();
        money.MapGet("/money.csv", CsvAsync).Produces(StatusCodes.Status200OK, contentType: "text/csv");
    }

    private static async Task<Money> GetAsync(HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken, StartedIn started = StartedIn.ThisYear)
    {
        var batches = await BatchesAsync(started, db, clock, cancellationToken);
        var registered = (await db.GetNurserySettingsAsync(cancellationToken)).VatRegistered;
        var counts = registered ? VatCounts.BeforeVat : VatCounts.WithVat;
        var (from, to) = Window(started, clock);
        var unbatched = await UnbatchedAsync(from, to, db, cancellationToken);

        return new Money(
            counts,
            registered,
            Totals(batches, unbatched.Values.Select(sale => sale.Takings)),
            batches.Count(batch => batch.CostKnown == CostKnown.NoAnswer),
            batches.Count(batch => batch.CostKnown == CostKnown.NotKnown),
            ByPlant(batches, unbatched),
            Group(batches, batch => batch.Started?.Year.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Not known"),
            Group(batches, batch => batch.Supplier ?? "Own seed and cuttings"),
            await TillAsync(from, to, db, clock, cancellationToken));
    }

    /// <summary>One row for each batch, in the order the figures show them; an unknown figure is an empty cell.</summary>
    private static async Task<IResult> CsvAsync(HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken, StartedIn started = StartedIn.All)
    {
        var batches = await BatchesAsync(started, db, clock, cancellationToken);
        var registered = (await db.GetNurserySettingsAsync(cancellationToken)).VatRegistered;
        string[] header =
        [
            "Batch", "Plant", "Variety", "How it started", "Started", "Supplier", "Plants started", "Lost", "Sold on invoices", "Sold from trays", "Still growing", "Cost known",
            .. Pairs("Seed or plants", "Pots and compost", "Other costs", "Total cost", "Cost for each plant", "Lost plants had cost", "Takings from invoices", "Takings from trays"),
            "Profit", "Profit worked out",
        ];
        var rows = batches.Select(batch => (object?[])
        [
            batch.Code, batch.CommonName, batch.Variety, Source(batch.Source), batch.Started, batch.Supplier, batch.PlantsStarted, batch.Lost,
            batch.Invoiced, batch.FromTrays + batch.NoPrice, batch.StillGrowing, Known(batch.CostKnown),
            .. Cells(batch.Seed), .. Cells(batch.Pots), .. Cells(batch.Others), .. Cells(batch.Total), .. Cells(batch.Each), .. Cells(batch.LostValue),
            .. Cells(batch.InvoiceTakings), .. Cells(batch.TrayTakings),
            batch.Profit is { } profit ? Round(registered ? profit.BeforeVat : profit.WithVat) : null,
            batch.Profit is null ? null : registered ? "Before VAT" : "With VAT",
        ]);

        return TypedResults.File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(Csv.Write(header, rows))).ToArray(), "text/csv", "hortiplanner-costs.csv");
    }

    private static IEnumerable<string> Pairs(params string[] names) => names.SelectMany(name => new[] { $"{name} with VAT", $"{name} before VAT" });

    private static object?[] Cells(MoneyPair? pair) => pair is null ? [null, null] : [Round(pair.WithVat), Round(pair.BeforeVat)];

    /// <summary>To the penny, always with two places, so £3 reads 3.00.</summary>
    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero) + 0.00m;

    private static string Source(BatchSource source) => source switch
    {
        BatchSource.SeedLot => "Seed",
        BatchSource.BoughtIn => "Bought in",
        _ => "Cuttings",
    };

    private static string Known(CostKnown known) => known switch
    {
        CostKnown.Yes => "Yes",
        CostKnown.Nothing => "Nothing",
        CostKnown.NotKnown => "Not known",
        _ => "No answer yet",
    };

    /// <summary>The calendar days a choice covers, for the till and for sales with no batch.</summary>
    private static (DateOnly From, DateOnly To) Window(StartedIn started, TimeProvider clock)
    {
        var year = clock.GetLocalNow().Year;
        return started switch
        {
            StartedIn.ThisYear => (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31)),
            StartedIn.LastYear => (new DateOnly(year - 1, 1, 1), new DateOnly(year - 1, 12, 31)),
            _ => (DateOnly.MinValue, DateOnly.MaxValue),
        };
    }

    /// <summary>Every batch started in the window, each with its costing worked out as Costs and takings does.</summary>
    private static async Task<List<BatchMoney>> BatchesAsync(StartedIn started, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var (from, to) = Window(started, clock);
        var facts = await db.Batches.AsNoTracking()
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new
            {
                batch.Id,
                plant.CommonName,
                plant.Variety,
                PlantId = plant.Id,
                Supplier = db.Suppliers.Where(supplier => supplier.Id == batch.SupplierId).Select(supplier => supplier.Name).FirstOrDefault(),
                StartedAt = db.EventLines.Where(line => line.BatchId == batch.Id).Join(db.Events, line => line.EventId, e => e.Id, (line, e) => e.OccurredAt).Min(at => (DateTimeOffset?)at),
            })
            .ToListAsync(cancellationToken);

        var rows = new List<BatchMoney>();

        foreach (var batch in facts
            .Select(batch => (Batch: batch, Day: batch.StartedAt is { } at ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone).DateTime) : (DateOnly?)null))
            .Where(row => started == StartedIn.All || (row.Day is { } day && day >= from && day <= to))
            .OrderBy(row => row.Day)
            .ThenBy(row => row.Batch.CommonName, StringComparer.OrdinalIgnoreCase))
        {
            if (await BatchCostings.CostingAsync(batch.Batch.Id, db, clock, cancellationToken) is { } costing)
            {
                rows.Add(BatchMoney.Of(costing, batch.Batch.PlantId, batch.Batch.CommonName, batch.Batch.Variety, batch.Batch.Supplier, batch.Day));
            }
        }

        return rows;
    }

    /// <summary>Plants sold on invoices without saying which batch, in the window: they count in the plant's takings only.</summary>
    private static async Task<Dictionary<Guid, Unbatched>> UnbatchedAsync(DateOnly from, DateOnly to, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        (await db.Invoices.AsNoTracking()
            .Where(invoice => invoice.Kind == InvoiceKind.Invoice && invoice.IssuedOn >= from && invoice.IssuedOn <= to && !db.Invoices.Any(credit => credit.CreditsInvoiceId == invoice.Id))
            .SelectMany(invoice => invoice.Lines.Where(line => line.PlantId != null && line.BatchId == null), (invoice, line) => new { line.PlantId, line.Quantity, line.UnitPrice, line.VatRate })
            .Join(db.Plants, line => line.PlantId, plant => plant.Id, (line, plant) => new { line.PlantId, plant.CommonName, plant.Variety, line.Quantity, line.UnitPrice, line.VatRate })
            .ToListAsync(cancellationToken))
        .GroupBy(line => line.PlantId!.Value)
        .ToDictionary(
            group => group.Key,
            group => new Unbatched(
                group.First().CommonName,
                group.First().Variety,
                group.Sum(line => line.Quantity),
                new MoneyPair(group.Sum(line => line.Quantity * line.UnitPrice * (1 + (line.VatRate / 100m))), group.Sum(line => line.Quantity * line.UnitPrice))));

    private sealed record Unbatched(string CommonName, string? Variety, int Quantity, MoneyPair Takings);

    private static MoneyPair Sum(IEnumerable<MoneyPair?> pairs)
    {
        var known = pairs.OfType<MoneyPair>().ToList();
        return new MoneyPair(known.Sum(pair => pair.WithVat), known.Sum(pair => pair.BeforeVat));
    }

    private static MoneyPair Less(MoneyPair a, MoneyPair b) => new(a.WithVat - b.WithVat, a.BeforeVat - b.BeforeVat);

    /// <summary>What the batches add up to. Profit counts only batches whose cost is known, with their takings.</summary>
    private static MoneyTotals Totals(List<BatchMoney> batches, IEnumerable<MoneyPair> unbatched)
    {
        var counted = batches.Where(batch => batch.Total is not null).ToList();
        var others = unbatched.ToList();
        var taken = Sum([.. batches.Select(batch => batch.Takings), .. others]);
        var spent = Sum(counted.Select(batch => batch.Total));
        var made = Less(Sum([.. counted.Select(batch => batch.Takings), .. others]), spent);
        return new MoneyTotals(taken, spent, made, Sum(batches.Select(batch => batch.LostValue)), batches.TrueForAll(batch => batch.CostKnown != CostKnown.NoAnswer));
    }

    /// <summary>
    /// Each plant: its batches added up, with sales that named no batch counted in its takings. A plant sold that way
    /// with no batch started in the time chosen still has a row, with only its takings.
    /// </summary>
    private static List<PlantMoney> ByPlant(List<BatchMoney> batches, Dictionary<Guid, Unbatched> unbatched)
    {
        var byPlant = batches.GroupBy(batch => batch.PlantId).ToDictionary(group => group.Key, group => group.ToList());

        return [.. byPlant.Keys.Union(unbatched.Keys)
            .Select(plantId => Plant(plantId, byPlant.GetValueOrDefault(plantId) ?? [], unbatched.GetValueOrDefault(plantId)))
            .OrderBy(plant => plant.CommonName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plant => plant.Variety, StringComparer.OrdinalIgnoreCase)];
    }

    private static PlantMoney Plant(Guid plantId, List<BatchMoney> rows, Unbatched? extra)
    {
        var counted = rows.Where(row => row.Total is not null).ToList();
        var complete = rows.TrueForAll(row => row.CostKnown != CostKnown.NoAnswer);
        var cost = Sum(counted.Select(row => row.Total));
        var alive = counted.Sum(row => Math.Max(row.Alive, 0));
        return new PlantMoney(
            plantId,
            rows.Count > 0 ? rows[0].CommonName : extra!.CommonName,
            rows.Count > 0 ? rows[0].Variety : extra!.Variety,
            rows.Count,
            rows.Sum(row => row.Invoiced + row.FromTrays + row.NoPrice) + (extra?.Quantity ?? 0),
            Sum([.. rows.Select(row => row.Takings), extra?.Takings]),
            extra?.Takings,
            counted.Count > 0 ? cost : null,
            complete,
            complete && counted.Count > 0 ? Less(Sum([.. counted.Select(row => row.Takings), extra?.Takings]), cost) : null,
            alive > 0 ? new MoneyPair(cost.WithVat / alive, cost.BeforeVat / alive) : null,
            rows.Any(row => row.LostValue is not null) ? Sum(rows.Select(row => row.LostValue)) : null);
    }

    private static List<GroupMoney> Group(List<BatchMoney> batches, Func<BatchMoney, string> key) =>
        [.. batches
            .GroupBy(key, StringComparer.Ordinal)
            .Select(group =>
            {
                var rows = group.ToList();
                var counted = rows.Where(row => row.Total is not null).ToList();
                var complete = rows.TrueForAll(row => row.CostKnown != CostKnown.NoAnswer);
                var cost = Sum(counted.Select(row => row.Total));
                return new GroupMoney(
                    group.Key,
                    Sum(rows.Select(row => row.Takings)),
                    counted.Count > 0 ? cost : null,
                    complete,
                    complete && counted.Count > 0 ? Less(Sum(counted.Select(row => row.Takings)), cost) : null);
            })
            .OrderBy(group => group.Name, StringComparer.Ordinal)];

    /// <summary>
    /// What the till took in the same days, beside what sales recorded from trays on those days came to, whichever
    /// batch they were from. Neither is said to be inside the other: a sale recorded from a tray need not have gone
    /// through the till.
    /// </summary>
    private static async Task<Till> TillAsync(DateOnly from, DateOnly to, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var till = await db.TillTakings.AsNoTracking()
            .Where(taking => taking.VoidedAt == null && taking.TakenOn >= from && taking.TakenOn <= to)
            .SumAsync(taking => (decimal?)taking.Amount, cancellationToken);

        var sales = db.Events.AsNoTracking().Where(e => e.Type == EventType.Sold && !db.Events.Any(correction => correction.CorrectsEventId == e.Id));

        if (from != DateOnly.MinValue)
        {
            var start = StartOf(from, clock);
            var end = StartOf(to.AddDays(1), clock);
            sales = sales.Where(e => e.OccurredAt >= start && e.OccurredAt < end);
        }

        var fromTrays = (await sales
            .Join(db.EventLines.Where(line => line.QuantityChange < 0), e => e.Id, line => line.EventId, (e, line) => new { line.QuantityChange, e.Payload })
            .ToListAsync(cancellationToken))
            .Where(row => WorkEndpoints.IsTraySale(row.Payload))
            .Select(row => Sales.TraySaleTakings(row.Payload, -row.QuantityChange))
            .ToList();

        return new Till(till ?? 0, Sum(fromTrays));
    }

    /// <summary>The first moment of a calendar day where the nursery is.</summary>
    private static DateTimeOffset StartOf(DateOnly day, TimeProvider clock)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, clock.LocalTimeZone.GetUtcOffset(midnight));
    }

    internal enum StartedIn
    {
        ThisYear,
        LastYear,
        All,
    }

    internal enum CostKnown
    {
        Yes,
        Nothing,
        NotKnown,
        NoAnswer,
    }

    /// <param name="Takings">Invoices and trays together, with VAT and before VAT.</param>
    /// <param name="Total">The whole cost, when known.</param>
    /// <param name="Profit">Takings less the whole cost, when the cost is known.</param>
    internal sealed record BatchMoney(
        Guid BatchId,
        string Code,
        Guid PlantId,
        string CommonName,
        string? Variety,
        BatchSource Source,
        DateOnly? Started,
        string? Supplier,
        CostKnown CostKnown,
        int PlantsStarted,
        int Lost,
        int Alive,
        int Invoiced,
        int FromTrays,
        int NoPrice,
        int StillGrowing,
        MoneyPair? Seed,
        MoneyPair? Pots,
        MoneyPair? Others,
        MoneyPair? Total,
        MoneyPair? Each,
        MoneyPair? LostValue,
        MoneyPair InvoiceTakings,
        MoneyPair TrayTakings,
        MoneyPair Takings,
        MoneyPair? Profit)
    {
        public static BatchMoney Of(BatchCostings.BatchCosting costing, Guid plantId, string commonName, string? variety, string? supplier, DateOnly? started)
        {
            var plant = costing.PerPlant;
            var takings = costing.Takings;
            var known = costing.Material.State switch
            {
                MaterialState.Amount or MaterialState.Purchase => CostKnown.Yes,
                MaterialState.Free or MaterialState.Cuttings => CostKnown.Nothing,
                MaterialState.NotKnown => CostKnown.NotKnown,
                _ => CostKnown.NoAnswer,
            };
            var others = costing.Others.Count > 0 ? Sum(costing.Others.Select(entry => entry.Cost)) : null;

            return new BatchMoney(
                costing.BatchId,
                costing.TraceabilityCode,
                plantId,
                commonName,
                variety,
                costing.Source,
                started,
                supplier,
                known,
                plant.Alive + plant.Lost - plant.Adjusted,
                plant.Lost,
                plant.Alive,
                takings.Invoiced,
                takings.FromTrays,
                takings.NoPrice,
                Math.Max(plant.Alive - takings.Sold, 0),
                costing.Material.Cost,
                costing.Pots?.Cost,
                others,
                costing.Total,
                plant.Each,
                plant.LostValue,
                takings.InvoiceTakings,
                takings.TrayTakings,
                takings.Total,
                costing.Total is { } total ? Less(takings.Total, total) : null);
        }
    }

    /// <param name="Complete">No batch is waiting for an answer, so the cost and profit are whole.</param>
    internal sealed record MoneyTotals(MoneyPair Taken, MoneyPair Spent, MoneyPair Made, MoneyPair Lost, bool Complete);

    /// <param name="Unbatched">Sold on invoices without saying which batch: in the takings, but not in any batch's.</param>
    /// <param name="Cost">What its batches with a known cost cost; "so far" when <paramref name="Complete"/> is false.</param>
    /// <param name="Profit">Only when every batch has an answer.</param>
    internal sealed record PlantMoney(
        Guid PlantId,
        string CommonName,
        string? Variety,
        int Batches,
        int Sold,
        MoneyPair Takings,
        MoneyPair? Unbatched,
        MoneyPair? Cost,
        bool Complete,
        MoneyPair? Profit,
        MoneyPair? Each,
        MoneyPair? LostValue);

    internal sealed record GroupMoney(string Name, MoneyPair Takings, MoneyPair? Cost, bool Complete, MoneyPair? Profit);

    /// <param name="Taken">What the till took in the same time, as its daily totals say.</param>
    /// <param name="FromTrays">Sales recorded from trays in the same time.</param>
    internal sealed record Till(decimal Taken, MoneyPair FromTrays);

    /// <param name="Unanswered">Batches nobody has said the cost of yet, which make the figures incomplete.</param>
    /// <param name="NotKnown">Batches said not to be known, left out of cost and profit.</param>
    internal sealed record Money(
        VatCounts Counts,
        bool VatRegistered,
        MoneyTotals Totals,
        int Unanswered,
        int NotKnown,
        IReadOnlyList<PlantMoney> Plants,
        IReadOnlyList<GroupMoney> ByYear,
        IReadOnlyList<GroupMoney> BySupplier,
        Till Till);
}
