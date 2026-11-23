using Hortiplanner.Api.Data;
using Hortiplanner.Api.Figures;
using MoneyPair = Hortiplanner.Api.Costs.BatchCostings.MoneyPair;

namespace Hortiplanner.Api.Costs;

/// <summary>
/// What each plant of a batch has cost, and what the lost ones had cost, from the same ledger Figures reads. Lost
/// plants' costs are carried by the plants still growing or sold; what they had cost is said for information, worked
/// out batch-wide as each loss happened, so it's never a second cost.
/// </summary>
internal static class PlantCosts
{
    /// <param name="total">The batch's whole cost, when known.</param>
    /// <param name="charges">Each part of the cost with the moment it was spent.</param>
    internal static PerPlant Work(
        List<BatchLedger.Row> rows, int absorbed, IEnumerable<BatchLedger.Absorbed> absorbedAt, bool uncounted,
        MoneyPair? total, IReadOnlyList<Charge> charges)
    {
        var counts = BatchLedger.CountsOf(rows, absorbed);
        var lostAll = BatchLedger.LostAll(rows);

        // Seed sown without a count has no number of plants until someone counts it, or it's all lost.
        if (uncounted && !lostAll)
        {
            return new PerPlant(counts.Alive, counts.Lost, counts.Adjusted, true, false, null, null);
        }

        // Every plant lost, and none gone at a count instead, including a tray lost before anyone counted it.
        var allLost = counts.Alive <= 0 && counts.Adjusted >= 0 && (counts.Lost > 0 || lostAll);

        if (total is null)
        {
            return new PerPlant(counts.Alive, counts.Lost, counts.Adjusted, false, allLost, null, null);
        }

        var each = counts.Alive > 0 ? new MoneyPair(total.WithVat / counts.Alive, total.BeforeVat / counts.Alive) : null;
        var lostValue = allLost ? total
            : counts.Lost == 0 ? null
            : LostValue(rows, absorbedAt, charges);
        return new PerPlant(counts.Alive, counts.Lost, counts.Adjusted, false, allLost, each, lostValue);
    }

    private sealed record Step(DateTimeOffset At, Guid Work, EventType Type, int Change);

    /// <summary>
    /// Walking the batch's work in the order it happened (a correction at the time of the work it replaces, and
    /// what a count absorbed at the count): at each loss, the plants lost take their share of what had been spent
    /// and not already lost, shared between the plants growing or sold just before it.
    /// </summary>
    private static MoneyPair LostValue(List<BatchLedger.Row> rows, IEnumerable<BatchLedger.Absorbed> absorbed, IReadOnlyList<Charge> charges)
    {
        // Sales leave the tray but still carry their share of the cost, so they don't change the plants counted.
        var steps = rows
            .Where(row => row.Type != EventType.Sold)
            .Select(row => new Step(row.Line.EffectiveAt ?? row.Event.OccurredAt, row.Event.Id, row.Type, row.Line.QuantityChange))
            .Concat(absorbed.Select(item => new Step(item.At, item.CountEventId, EventType.StockCounted, item.Change)))
            .GroupBy(step => (step.At, step.Work))
            .Select(group => new Step(group.Key.At, group.Key.Work, group.First().Type, group.Sum(step => step.Change)))
            .OrderBy(step => step.At)
            .ThenBy(step => step.Work);

        var alive = 0;
        decimal spentWith = 0, spentBefore = 0, lostWith = 0, lostBefore = 0;
        var pending = new Queue<Charge>(charges.OrderBy(charge => charge.At));

        foreach (var step in steps)
        {
            // Everything spent up to this moment counts against plants lost now.
            while (pending.TryPeek(out var charge) && charge.At <= step.At)
            {
                pending.Dequeue();
                spentWith += charge.Cost.WithVat;
                spentBefore += charge.Cost.BeforeVat;
            }

            if (step.Type == EventType.Lost && step.Change < 0 && alive > 0)
            {
                var share = (decimal)Math.Min(-step.Change, alive) / alive;
                lostWith += spentWith * share;
                lostBefore += spentBefore * share;
                spentWith -= spentWith * share;
                spentBefore -= spentBefore * share;
            }

            alive = Math.Max(alive + step.Change, 0);
        }

        return new MoneyPair(lostWith, lostBefore);
    }

    /// <summary>Something charged to a batch at a moment, for working out what lost plants had cost by then.</summary>
    internal sealed record Charge(DateTimeOffset At, MoneyPair Cost);

    /// <param name="Alive">Still growing or sold.</param>
    /// <param name="Adjusted">More (or, below nought, fewer) found at stock counts than the work said. Not losses.</param>
    /// <param name="NotCounted">Sown without a count and not counted since, so there's no number to share the cost between.</param>
    /// <param name="Each">The cost for each plant still growing or sold, unrounded.</param>
    /// <param name="LostValue">What the lost plants had cost between them, when any were lost.</param>
    /// <param name="AllLost">Every plant was lost, so the whole cost went with them.</param>
    internal sealed record PerPlant(int Alive, int Lost, int Adjusted, bool NotCounted, bool AllLost, MoneyPair? Each, MoneyPair? LostValue);
}
