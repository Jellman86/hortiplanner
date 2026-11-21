using System.Globalization;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Catalogue;
using Hortiplanner.Api.Data;
using BatchFacts = Hortiplanner.Api.Figures.BatchLedger.BatchFacts;
using EventFacts = Hortiplanner.Api.Figures.BatchLedger.EventFacts;
using Row = Hortiplanner.Api.Figures.BatchLedger.Row;

namespace Hortiplanner.Api.Figures;

/// <summary>Germination and survival, worked out from events each time, so corrections are always reflected.</summary>
internal static class FigureEndpoints
{
    private static readonly EventType[] StartingTypes = BatchLedger.StartingTypes;

    public static void MapFigureEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/figures", GetFiguresAsync).RequireAuthorization(AccountServices.OwnerPolicy);

    private static string? LossReasonOf(string payload) => BatchLedger.LossReasonOf(payload);

    private static async Task<Figures> GetFiguresAsync(HortiplannerDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        var ledger = await BatchLedger.LoadAsync(db, cancellationToken);
        var (events, reversed, batches, lines) = (ledger.Events, ledger.Reversed, ledger.Batches, ledger.Rows);
        var absorbed = ledger.Absorbed
            .GroupBy(entry => batches[entry.Key].PlantId)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Value));

        var germinatedBatches = GerminatedBatches(events, reversed, lines, batches);

        var perPlant = lines
            .GroupBy(row => batches[row.Line.BatchId].PlantId)
            .Select(group => ForPlant(batches[group.First().Line.BatchId], group.ToList(), germinatedBatches, absorbed.GetValueOrDefault(group.Key)))
            .OrderBy(figures => figures.CommonName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(figures => figures.Variety, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var losses = lines
            .Where(row => row.Type == EventType.Lost)
            .GroupBy(row => LossReasonOf(row.Event.Payload) ?? nameof(LossReason.Other), StringComparer.Ordinal)
            // A tray lost altogether before anyone counted it has no number to add, so it's counted as a tray, once.
            .Select(group => new LossFigures(
                group.Key,
                -group.Sum(row => row.Line.QuantityChange),
                group.Where(row => row.Line.QuantityChange == 0 && EventStore.LostAll(row.Event.Payload)).Select(row => row.Line.ContainerId).Distinct().Count()))
            .OrderByDescending(figures => figures.Quantity)
            .ThenByDescending(figures => figures.UncountedTrays)
            .ToList();

        var (bySupplier, byPlace, bySeason) = await BreakdownsAsync(lines, batches, germinatedBatches, db, store, cancellationToken);
        return new Figures(perPlant, losses, bySupplier, byPlace, bySeason);
    }

    /// <summary>
    /// Seed batches that count towards germination: only once someone has recorded that they germinated. Seed sown by
    /// weight, by the packet or not known has no number of seeds to measure against, so it gives no rate.
    /// </summary>
    private static HashSet<Guid> GerminatedBatches(Dictionary<Guid, EventFacts> events, HashSet<Guid> reversed, List<Row> lines, Dictionary<Guid, BatchFacts> batches)
    {
        var germinatedContainers = events.Values
            .Where(e => e.Type == EventType.Germinated && !reversed.Contains(e.Id) && e.ContainerId is not null)
            .Select(e => e.ContainerId!.Value)
            .ToHashSet();

        // A tray where none came up, recorded as all lost for that reason, is a real nought.
        var noneCameUp = lines
            .Where(row => row.Type == EventType.Lost
                && EventStore.LostAll(row.Event.Payload)
                && string.Equals(LossReasonOf(row.Event.Payload), nameof(LossReason.FailedToGerminate), StringComparison.Ordinal))
            .Select(row => row.Line.BatchId);

        // "About so many" can only be measured once someone has counted what came up, or none did.
        var settled = events.Values
            .Where(e => e.Type == EventType.StockCounted && !reversed.Contains(e.Id) && EventStore.CountOf(e.Payload) is not null)
            .Select(e => EventStore.CountOf(e.Payload)!.BatchId)
            .Concat(noneCameUp)
            .ToHashSet();

        return lines
            .Where(row => row.Type == EventType.Sown && germinatedContainers.Contains(row.Line.ContainerId))
            .Select(row => row.Line.BatchId)
            .Concat(noneCameUp)
            .Where(id => batches[id].SownAs switch
            {
                null or SownMeasure.Counted => true,
                SownMeasure.About => settled.Contains(id),
                _ => false,
            })
            .ToHashSet();
    }

    /// <summary>Survival and germination by supplier, by place, and by the month batches were started.</summary>
    private static async Task<(List<GroupFigures>, List<GroupFigures>, List<GroupFigures>)> BreakdownsAsync(
        List<Row> lines,
        Dictionary<Guid, BatchFacts> batches,
        HashSet<Guid> germinatedBatches,
        HortiplannerDbContext db,
        EventStore store,
        CancellationToken cancellationToken)
    {
        var places = await TopPlacesAsync(lines, db, store, cancellationToken);
        var started = lines
            .Where(row => StartingTypes.Contains(row.Type))
            .GroupBy(row => row.Line.BatchId)
            .ToDictionary(group => group.Key, group => group.Min(row => row.Event.OccurredAt));

        return (
            Group(lines, row => batches[row.Line.BatchId].SupplierName ?? "Own seed and cuttings", germinatedBatches),
            Group(lines, row => places.GetValueOrDefault(row.Line.ContainerId) ?? "No place recorded", germinatedBatches),
            [.. Group(lines, row => started.TryGetValue(row.Line.BatchId, out var at) ? at.ToString("yyyy-MM", CultureInfo.InvariantCulture) : null, germinatedBatches)
                .OrderBy(group => group.Name, StringComparer.Ordinal)]);
    }

    /// <summary>The greenhouse, tunnel or bed each container is in: the first place below the site.</summary>
    private static async Task<Dictionary<Guid, string>> TopPlacesAsync(List<Row> lines, HortiplannerDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        var paths = await PlacePaths.LoadAsync(db, cancellationToken);
        var placements = await store.GetPlacementsAsync([.. lines.Select(row => row.Line.ContainerId).Distinct()], cancellationToken);

        return placements
            .Where(entry => entry.Value.LocationId is { } id && paths.ContainsKey(id))
            .ToDictionary(entry => entry.Key, entry => paths[entry.Value.LocationId!.Value].Split(", ")[0]);
    }

    /// <summary>Survival and germination for each group of rows, largest first.</summary>
    private static List<GroupFigures> Group(List<Row> rows, Func<Row, string?> key, HashSet<Guid> germinatedBatches) =>
        rows
            .Select(row => (Row: row, Key: key(row)))
            .Where(item => item.Key is not null)
            .GroupBy(item => item.Key!, StringComparer.Ordinal)
            .Select(group =>
            {
                var measured = Measure([.. group.Select(item => item.Row)], germinatedBatches);
                return new GroupFigures(group.Key, measured.Started, measured.Lost, measured.GerminationRate, measured.SurvivalRate, measured.Approximate);
            })
            .Where(group => group.Started > 0)
            .OrderByDescending(group => group.Started)
            .ToList();

    private static Measured Measure(List<Row> rows, HashSet<Guid> germinatedBatches)
    {
        // The same counts every batch's costing reads, so the two can't disagree.
        var (started, lost, _) = BatchLedger.CountsOf(rows, 0);
        var watched = rows.Where(row => germinatedBatches.Contains(row.Line.BatchId)).ToList();

        // Counted sowings: seeds sown, less those that failed to germinate.
        var sown = watched.Where(row => row.Type == EventType.Sown && row.Line.QuantityChange > 0).Sum(row => row.Line.QuantityChange);
        var failed = -watched
            .Where(row => row.Type == EventType.Lost && string.Equals(LossReasonOf(row.Event.Payload), nameof(LossReason.FailedToGerminate), StringComparison.Ordinal))
            .Sum(row => row.Line.QuantityChange);

        // "About so many" sowings: seedlings at the first count against the seeds thought sown, so approximate.
        var about = watched
            .Where(row => row.SownAbout is not null)
            .Select(row => (Seeds: row.SownAbout!.Value, Up: watched.Where(other => other.FirstCount && other.Line.BatchId == row.Line.BatchId).Sum(other => other.StartCount)))
            .ToList();
        var aboutSeeds = about.Sum(item => item.Seeds);
        var seeds = sown + aboutSeeds;
        // Each batch is capped at its own estimate, so one that beat it can't hide failures elsewhere.
        var up = sown - failed + about.Sum(item => Math.Min(item.Up, item.Seeds));

        return new(
            started,
            lost,
            seeds > 0 ? Math.Min(1, (double)up / (double)seeds) : null,
            // A first count that was only a rough guess makes the rate a guess too.
            aboutSeeds > 0 || watched.Any(row => row.FirstCount && row.Type == EventType.StockCounted && EventStore.IsRough(row.Event.Payload)),
            started > 0 ? (started - lost) / (double)started : null);
    }

    private static PlantFigures ForPlant(BatchFacts batch, List<Row> rows, HashSet<Guid> germinatedBatches, int absorbed)
    {
        var measured = Measure(rows, germinatedBatches);
        var adjusted = BatchLedger.CountsOf(rows, absorbed).Adjusted;

        return new PlantFigures(
            batch.PlantId,
            batch.CommonName,
            batch.Variety,
            measured.Started,
            measured.Lost,
            adjusted,
            measured.Started - measured.Lost + adjusted,
            measured.GerminationRate,
            measured.SurvivalRate,
            measured.Approximate);
    }

    private sealed record Measured(int Started, int Lost, double? GerminationRate, bool Approximate, double? SurvivalRate);

    internal sealed record PlantFigures(
        Guid PlantId,
        string CommonName,
        string? Variety,
        int Started,
        int Lost,
        int Adjusted,
        int Alive,
        double? GerminationRate,
        double? SurvivalRate,
        bool GerminationApproximate = false);

    /// <param name="UncountedTrays">Trays lost altogether for this reason before anyone counted them, so not in Quantity.</param>
    internal sealed record LossFigures(string Reason, int Quantity, int UncountedTrays);

    /// <param name="Name">A supplier, a place such as "Greenhouse 2", or a month such as "2026-11".</param>
    /// <param name="GerminationApproximate">Whether the rate rests on seed sown "about so many", or on a rough first count.</param>
    internal sealed record GroupFigures(string Name, int Started, int Lost, double? GerminationRate, double? SurvivalRate, bool GerminationApproximate = false);

    internal sealed record Figures(
        IReadOnlyList<PlantFigures> Plants,
        IReadOnlyList<LossFigures> Losses,
        IReadOnlyList<GroupFigures> BySupplier,
        IReadOnlyList<GroupFigures> ByPlace,
        IReadOnlyList<GroupFigures> BySeason);
}
