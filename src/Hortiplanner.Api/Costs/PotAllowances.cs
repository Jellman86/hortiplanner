using System.Security.Claims;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Settings;
using Microsoft.EntityFrameworkCore;
using MoneyPair = Hortiplanner.Api.Costs.BatchCostings.MoneyPair;

namespace Hortiplanner.Api.Costs;

/// <summary>
/// What it costs to pot one plant into each size, set under Settings, Pot sizes, and what potting has cost a batch.
/// The owner's alone: staff never see an allowance.
/// </summary>
internal static class PotAllowances
{
    private const decimal MostEach = 999.99m;

    public static void MapPotAllowanceEndpoints(this IEndpointRouteBuilder app)
    {
        var allowances = app.MapGroup("/api/pot-sizes").RequireAuthorization(AccountServices.OwnerPolicy).RequireFeature(NurseryFeature.Costs);
        allowances.MapGet("/allowances", ListAsync);
        allowances.MapPut("/{id:guid}/allowance", SetAsync).Produces<AllowanceSaved>();
    }

    /// <summary>The allowance a row stands for, with VAT and before VAT, worked out exactly; rounded only for showing.</summary>
    internal static MoneyPair Each(PotSizeCost row)
    {
        var cost = row.CostEach ?? 0;
        var factor = 1 + (row.VatRate / 100m);
        return row.IncludesVat ? new MoneyPair(cost, cost / factor) : new MoneyPair(cost * factor, cost);
    }

    /// <summary>
    /// The allowance for a size on a day: the newest set on or before it, the later of two on one day. Before the
    /// first day one was set, the amount that stood at the end of that day is used, so a first amount covers potting
    /// already recorded, and a slip put right the same day is put right for that potting too.
    /// </summary>
    internal static PotSizeCost? On(IReadOnlyList<PotSizeCost> rows, DateOnly day) =>
        rows.Where(row => row.From <= day).OrderByDescending(row => row.From).ThenByDescending(row => row.SetAt).ThenByDescending(row => row.Id).FirstOrDefault()
        ?? rows.OrderBy(row => row.From).ThenByDescending(row => row.SetAt).ThenByDescending(row => row.Id).FirstOrDefault();

    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private static async Task<List<PotAllowance>> ListAsync(HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var sizes = await db.PotSizes.AsNoTracking().OrderBy(size => size.Name).ToListAsync(cancellationToken);
        var rows = (await db.PotSizeCosts.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(row => row.PotSizeId);
        var today = Today(clock);

        return sizes.ConvertAll(size =>
        {
            var current = On([.. rows[size.Id]], today);
            return new PotAllowance(
                size.Id,
                size.Name,
                current?.CostEach,
                current?.IncludesVat ?? true,
                current?.VatRate ?? 0,
                current is { CostEach: not null } ? Each(current) : null,
                rows[size.Id].Any());
        });
    }

    /// <summary>A new amount from today, at today's standard rate. The same amount again changes nothing.</summary>
    private static async Task<IResult> SetAsync(Guid id, AllowanceRequest request, ClaimsPrincipal user, HortiplannerDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (request.CostEach is not null && (request.CostEach is not (> 0 and <= MostEach) || decimal.Round(request.CostEach.Value, 2) != request.CostEach))
        {
            return TypedResults.BadRequest();
        }

        if (!await db.PotSizes.AnyAsync(size => size.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRequestAsync(id, cancellationToken);
        var rows = await db.PotSizeCosts.AsNoTracking().Where(row => row.PotSizeId == id).ToListAsync(cancellationToken);
        var today = Today(clock);
        var current = On(rows, today);
        var rate = await VatRates.PercentAsync(db, VatBand.Standard, today, cancellationToken);

        // The same amount at a standard rate that has changed since is brought up to today's rate.
        if (current?.CostEach == request.CostEach && (request.CostEach is null || (current?.IncludesVat == request.IncludesVat && current?.VatRate == rate)))
        {
            return TypedResults.Ok(new AllowanceSaved(Changed: false, First: false, ReachesBack: false));
        }

        db.PotSizeCosts.Add(new PotSizeCost
        {
            PotSizeId = id,
            CostEach = request.CostEach,
            IncludesVat = request.IncludesVat,
            VatRate = rate,
            From = today,
            SetBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // Every amount so far is from today, so this one also covers potting recorded before today.
        return TypedResults.Ok(new AllowanceSaved(Changed: true, First: rows.Count == 0, ReachesBack: rows.TrueForAll(row => row.From == today)));
    }

    /// <summary>
    /// What potting has cost a batch: for each time plants of it were pricked out or potted on into a size, how many
    /// times that size's allowance on that day. Undone potting isn't charged, and a corrected one is charged as
    /// corrected. Sizes with no allowance, and potting with no size, are named rather than charged.
    /// </summary>
    internal static async Task<PotsCost?> ForBatchAsync(Guid batchId, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var potting = await PottingAsync(batchId, db, cancellationToken);

        if (potting.Count == 0)
        {
            return null;
        }

        var rows = (await db.PotSizeCosts.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(row => row.PotSizeId);
        var names = await db.PotSizes.AsNoTracking().ToDictionaryAsync(size => size.Id, size => size.Name, cancellationToken);
        var charged = potting
            .Where(pot => pot.PotSizeId is not null)
            .Select(pot => new { Pot = pot, Allowance = On([.. rows[pot.PotSizeId!.Value]], DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(pot.OccurredAt, clock.LocalTimeZone).DateTime)) })
            .ToList();
        var lines = charged
            .Where(row => row.Allowance is { CostEach: not null })
            .OrderBy(row => names[row.Pot.PotSizeId!.Value], StringComparer.Ordinal)
            .ThenBy(row => row.Allowance!.From)
            .ThenBy(row => row.Allowance!.SetAt)
            .GroupBy(row => row.Allowance!.Id)
            .Select(group =>
            {
                var each = Each(group.First().Allowance!);
                var plants = group.Sum(row => row.Pot.Plants);
                return new PotLine(names[group.First().Pot.PotSizeId!.Value], plants, each, new MoneyPair(each.WithVat * plants, each.BeforeVat * plants));
            })
            .ToList();

        return new PotsCost(
            new MoneyPair(lines.Sum(line => line.Cost.WithVat), lines.Sum(line => line.Cost.BeforeVat)),
            lines,
            [.. charged.Where(row => row.Allowance is not { CostEach: not null }).Select(row => names[row.Pot.PotSizeId!.Value]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            potting.Where(pot => pot.PotSizeId is null).Sum(pot => pot.Plants));
    }

    private sealed record Potting(DateTimeOffset OccurredAt, Guid? PotSizeId, int Plants);

    /// <summary>Plants of the batch moved into a new pot or tray, as the work stands now.</summary>
    private static async Task<List<Potting>> PottingAsync(Guid batchId, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.EventLines.AsNoTracking()
            .Where(line => line.BatchId == batchId && line.QuantityChange > 0)
            .Join(db.Events, line => line.EventId, e => e.Id, (line, e) => new { line.ContainerId, line.QuantityChange, e.Id, e.Type, Into = e.ContainerId, e.PotSizeId, e.OccurredAt, e.CorrectsEventId })
            .Where(row => row.ContainerId == row.Into)
            .ToListAsync(cancellationToken);
        var ids = rows.Select(row => row.Id).Concat(rows.Where(row => row.CorrectsEventId != null).Select(row => row.CorrectsEventId!.Value)).Distinct().ToList();
        var reversed = (await db.Events.AsNoTracking().Where(e => e.CorrectsEventId != null && ids.Contains(e.CorrectsEventId.Value)).Select(e => e.CorrectsEventId!.Value).ToListAsync(cancellationToken)).ToHashSet();
        var originals = await db.Events.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => new { e.Type, e.OccurredAt }, cancellationToken);

        return [.. rows
            .Where(row => !reversed.Contains(row.Id))
            .Select(row => row.Type == EventType.Corrected && row.CorrectsEventId is { } of && originals.TryGetValue(of, out var original)
                ? new { Type = original.Type, original.OccurredAt, row.PotSizeId, row.QuantityChange }
                : new { row.Type, row.OccurredAt, row.PotSizeId, row.QuantityChange })
            .Where(row => row.Type is EventType.PrickedOut or EventType.PottedOn)
            .Select(row => new Potting(row.OccurredAt, row.PotSizeId, row.QuantityChange))];
    }

    internal sealed record AllowanceRequest(decimal? CostEach, bool IncludesVat = true);

    /// <param name="First">The first amount ever for this size.</param>
    /// <param name="ReachesBack">It also covers potting recorded before today, as every amount so far was set today.</param>
    internal sealed record AllowanceSaved(bool Changed, bool First, bool ReachesBack);

    /// <param name="Each">The amount for one plant, with VAT and before VAT, unrounded.</param>
    /// <param name="Set">Whether an amount has ever been set, so a first one can be told apart from a change.</param>
    internal sealed record PotAllowance(Guid PotSizeId, string Name, decimal? CostEach, bool IncludesVat, decimal VatRate, MoneyPair? Each, bool Set);

    internal sealed record PotLine(string Size, int Plants, MoneyPair Each, MoneyPair Cost);

    /// <param name="WithoutAllowance">Sizes plants were potted into that have no allowance, so aren't charged.</param>
    /// <param name="UnsizedPlants">Plants potted with no pot size recorded.</param>
    internal sealed record PotsCost(MoneyPair Cost, IReadOnlyList<PotLine> Lines, IReadOnlyList<string> WithoutAllowance, int UnsizedPlants);
}
