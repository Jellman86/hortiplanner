using System.Text.Json;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Figures;

/// <summary>
/// The work on batches as it stands now, shared by Figures and each batch's costing so the two can never disagree
/// about what counts as started, lost or found at a count. Undone work is left out, a correction counts as the work
/// it corrected, and the first count of seed sown without one is where that batch starts.
/// </summary>
internal static class BatchLedger
{
    internal static readonly EventType[] StartingTypes = [EventType.Sown, EventType.Received, EventType.CuttingsTaken];

    internal static string? LossReasonOf(string payload)
    {
        using var details = JsonDocument.Parse(payload);
        return details.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() : null;
    }

    /// <summary>Every batch's work.</summary>
    internal static async Task<Ledger> LoadAsync(HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var events = await db.Events
            .AsNoTracking()
            .Select(e => new EventFacts(e.Id, e.Type, e.CorrectsEventId, e.ContainerId, e.Payload, e.OccurredAt))
            .ToDictionaryAsync(e => e.Id, cancellationToken);
        var batches = await BatchesAsync(db.Batches.AsNoTracking(), db, cancellationToken);
        var lines = await db.EventLines.AsNoTracking().ToListAsync(cancellationToken);
        var adjustments = await db.CountAdjustments.AsNoTracking().ToListAsync(cancellationToken);
        return Build(events, batches, lines, adjustments);
    }

    /// <summary>One batch's work, read without the rest of the nursery's.</summary>
    internal static async Task<Ledger> LoadAsync(HortiplannerDbContext db, Guid batchId, CancellationToken cancellationToken)
    {
        var lines = await db.EventLines.AsNoTracking().Where(line => line.BatchId == batchId).ToListAsync(cancellationToken);
        var touched = lines.Select(line => line.EventId).Distinct().ToList();

        // The batch's events, anything correcting them, and whatever a correction corrected, to know what it stands for.
        var events = await db.Events.AsNoTracking()
            .Where(e => touched.Contains(e.Id) || (e.CorrectsEventId != null && touched.Contains(e.CorrectsEventId.Value)))
            .Select(e => new EventFacts(e.Id, e.Type, e.CorrectsEventId, e.ContainerId, e.Payload, e.OccurredAt))
            .ToDictionaryAsync(e => e.Id, cancellationToken);
        var originals = events.Values.Where(e => e.CorrectsEventId is { } of && !events.ContainsKey(of)).Select(e => e.CorrectsEventId!.Value).ToList();

        foreach (var original in await db.Events.AsNoTracking()
            .Where(e => originals.Contains(e.Id))
            .Select(e => new EventFacts(e.Id, e.Type, e.CorrectsEventId, e.ContainerId, e.Payload, e.OccurredAt))
            .ToListAsync(cancellationToken))
        {
            events[original.Id] = original;
        }

        var batches = await BatchesAsync(db.Batches.AsNoTracking().Where(batch => batch.Id == batchId), db, cancellationToken);
        var adjustments = await db.CountAdjustments.AsNoTracking().Where(adjustment => adjustment.BatchId == batchId).ToListAsync(cancellationToken);
        return Build(events, batches, lines, adjustments);
    }

    private static async Task<Dictionary<Guid, BatchFacts>> BatchesAsync(IQueryable<Batch> query, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await query
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new BatchFacts(
                batch.Id,
                batch.PlantId,
                plant.CommonName,
                plant.Variety,
                db.Suppliers.Where(supplier => supplier.Id == batch.SupplierId).Select(supplier => supplier.Name).FirstOrDefault(),
                batch.SownAs,
                batch.SownAmount))
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);

    private static Ledger Build(Dictionary<Guid, EventFacts> events, Dictionary<Guid, BatchFacts> batches, List<EventLine> lines, List<CountAdjustment> adjustments)
    {
        var reversed = events.Values.Where(e => e.CorrectsEventId is not null).Select(e => e.CorrectsEventId!.Value).ToHashSet();
        var rows = FirstCountsAsStarts(
            lines
                .Where(line => !reversed.Contains(line.EventId) && batches.ContainsKey(line.BatchId))
                .Select(line => new Row(line, events[line.EventId], WorkDoneBy(events[line.EventId], events)))
                .ToList(),
            batches);

        // Changes stock counts absorbed when earlier work arrived late or was undone: part of each count's adjustment.
        var absorbed = adjustments
            .Where(adjustment => !reversed.Contains(adjustment.EventId) && !reversed.Contains(adjustment.CountEventId) && batches.ContainsKey(adjustment.BatchId))
            .GroupBy(adjustment => adjustment.BatchId)
            .ToDictionary(group => group.Key, group => group.Sum(adjustment => adjustment.QuantityChange));

        return new Ledger(events, reversed, batches, rows, absorbed);
    }

    /// <summary>
    /// Seed sown without a count is first counted later, so that count is where it starts for the figures, not an
    /// adjustment. Rows for "about so many" sowings carry the seeds thought sown.
    /// </summary>
    private static List<Row> FirstCountsAsStarts(List<Row> rows, Dictionary<Guid, BatchFacts> batches)
    {
        var firsts = rows
            .Where(row => row.Type == EventType.StockCounted && batches[row.Line.BatchId].SownAs is not (null or SownMeasure.Counted))
            .GroupBy(row => row.Line.BatchId)
            .Select(group => group.OrderBy(row => row.Event.OccurredAt).First())
            .ToHashSet();

        // Only the sowing carries the seeds, so a tray pricked out elsewhere doesn't count them again.
        return rows.ConvertAll(row => row with
        {
            FirstCount = firsts.Contains(row),
            StartCount = firsts.Contains(row) ? EventStore.CountOf(row.Event.Payload)?.Counted ?? row.Line.QuantityChange : 0,
            SownAbout = row.Type == EventType.Sown && batches[row.Line.BatchId] is { SownAs: SownMeasure.About, SownAmount: { } amount } ? (int)amount : null,
        });
    }

    /// <summary>A correction stands in for the event it reversed, so it counts as that kind of work.</summary>
    private static EventType WorkDoneBy(EventFacts work, Dictionary<Guid, EventFacts> events)
    {
        while (work.Type == EventType.Corrected)
        {
            work = events[work.CorrectsEventId!.Value];
        }

        return work.Type;
    }

    /// <summary>How many plants started, were lost, and were found more or fewer at a count, from some rows.</summary>
    internal static Counts CountsOf(IReadOnlyCollection<Row> rows, int absorbed)
    {
        var started = rows.Where(row => StartingTypes.Contains(row.Type)).Sum(row => row.Line.QuantityChange) + rows.Where(row => row.FirstCount).Sum(row => row.StartCount);
        var lost = -rows.Where(row => row.Type == EventType.Lost).Sum(row => row.Line.QuantityChange);

        // A first count's own line can differ from what it counted, if an earlier count was undone: the rest is adjustment.
        var adjusted = rows.Where(row => row.Type == EventType.StockCounted).Sum(row => row.FirstCount ? row.Line.QuantityChange - row.StartCount : row.Line.QuantityChange) + absorbed;
        return new Counts(started, lost, adjusted);
    }

    /// <param name="Absorbed">For each batch, stock changes counts absorbed: part of each count's adjustment.</param>
    internal sealed record Ledger(
        Dictionary<Guid, EventFacts> Events,
        HashSet<Guid> Reversed,
        Dictionary<Guid, BatchFacts> Batches,
        List<Row> Rows,
        Dictionary<Guid, int> Absorbed);

    /// <param name="Alive">Still growing or sold: started, less lost, give or take what counts found.</param>
    internal sealed record Counts(int Started, int Lost, int Adjusted)
    {
        public int Alive => Started - Lost + Adjusted;
    }

    internal sealed record BatchFacts(Guid Id, Guid PlantId, string CommonName, string? Variety, string? SupplierName, SownMeasure? SownAs, decimal? SownAmount);

    /// <param name="FirstCount">The first count of seed sown without one: where it starts, for the figures.</param>
    /// <param name="SownAbout">For "about so many" sowings, the seeds thought sown.</param>
    /// <param name="StartCount">For a first count, the number it counted: where the batch starts.</param>
    internal sealed record Row(EventLine Line, EventFacts Event, EventType Type, bool FirstCount = false, int? SownAbout = null, int StartCount = 0);

    internal sealed record EventFacts(Guid Id, EventType Type, Guid? CorrectsEventId, Guid? ContainerId, string Payload, DateTimeOffset OccurredAt);
}
