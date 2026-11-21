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
    /// <param name="charges">Each part of the cost with the moment it was spent, oldest first.</param>
    internal static PerPlant Work(BatchLedger.Ledger ledger, Guid batchId, MoneyPair? total, IReadOnlyList<PotAllowances.Charge> charges)
    {
        var rows = ledger.Rows.Where(row => row.Line.BatchId == batchId).ToList();
        var counts = BatchLedger.CountsOf(rows, ledger.Absorbed.GetValueOrDefault(batchId));
        var batch = ledger.Batches[batchId];

        // Seed sown without a count has no number of plants until someone counts it.
        var notCounted = batch.SownAs is not (null or SownMeasure.Counted) && !rows.Exists(row => row.FirstCount);

        if (notCounted || total is null)
        {
            return new PerPlant(counts.Alive, counts.Lost, counts.Adjusted, notCounted, null, null);
        }

        var each = counts.Alive > 0 ? new MoneyPair(total.WithVat / counts.Alive, total.BeforeVat / counts.Alive) : null;
        var lostValue = counts.Lost == 0 ? null : counts.Alive <= 0 ? total : LostValue(rows, charges);
        return new PerPlant(counts.Alive, counts.Lost, counts.Adjusted, false, each, lostValue);
    }

    /// <summary>
    /// Walking the batch's work in order: at each loss, the plants lost times what had been spent so far, shared
    /// between the plants growing or sold just before it.
    /// </summary>
    private static MoneyPair LostValue(List<BatchLedger.Row> rows, IReadOnlyList<PotAllowances.Charge> charges)
    {
        var alive = 0;
        decimal spentWith = 0, spentBefore = 0, lostWith = 0, lostBefore = 0;
        var pending = new Queue<PotAllowances.Charge>(charges.OrderBy(charge => charge.At));

        foreach (var work in rows.GroupBy(row => row.Event.Id).OrderBy(group => group.First().Event.OccurredAt))
        {
            // Everything spent up to this moment counts against plants lost now.
            while (pending.TryPeek(out var charge) && charge.At <= work.First().Event.OccurredAt)
            {
                pending.Dequeue();
                spentWith += charge.Cost.WithVat;
                spentBefore += charge.Cost.BeforeVat;
            }

            var type = work.First().Type;
            var change = work.Sum(row => row.FirstCount ? row.StartCount : row.Line.QuantityChange);

            if (type == EventType.Lost && change < 0 && alive > 0)
            {
                var lost = Math.Min(-change, alive);
                lostWith += spentWith * lost / alive;
                lostBefore += spentBefore * lost / alive;
                spentWith -= spentWith * lost / alive;
                spentBefore -= spentBefore * lost / alive;
            }

            // Sales still count: a plant sold carried its share of the cost.
            alive += type == EventType.Sold ? 0 : change;
            alive = Math.Max(alive, 0);
        }

        return new MoneyPair(lostWith, lostBefore);
    }

    /// <param name="Alive">Still growing or sold.</param>
    /// <param name="Adjusted">More (or, below nought, fewer) found at stock counts than the work said. Not losses.</param>
    /// <param name="NotCounted">Sown without a count and not counted since, so there's no number to share the cost between.</param>
    /// <param name="Each">The cost for each plant still growing or sold, unrounded.</param>
    /// <param name="LostValue">What the lost plants had cost between them, when any were lost.</param>
    internal sealed record PerPlant(int Alive, int Lost, int Adjusted, bool NotCounted, MoneyPair? Each, MoneyPair? LostValue);
}
