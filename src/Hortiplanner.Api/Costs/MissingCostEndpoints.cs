using System.Security.Claims;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Costs;

/// <summary>
/// Costs to fill in: the batches nobody has said the cost of yet, for the owner to answer one by one or say, all at
/// once, that nobody knows.
/// </summary>
internal static class MissingCostEndpoints
{
    /// <summary>More batches than a nursery would have waiting, so one request can't hold the locks for long.</summary>
    private const int MostAtOnce = 2000;

    public static void MapMissingCostEndpoints(this IEndpointRouteBuilder app)
    {
        var costs = app.MapGroup("/api/costs").RequireAuthorization(AccountServices.OwnerPolicy).RequireFeature(NurseryFeature.Costs);
        costs.MapGet("/missing", ListAsync);
        costs.MapPost("/not-known", NotKnownAsync).Produces<MarkedNotKnown>();
    }

    /// <summary>Each batch with no answer, oldest first, with the trays it's in now.</summary>
    private static async Task<List<MissingCost>> ListAsync(HortiplannerDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        var missing = await BatchCostings.MissingAsync(db, await db.Batches.Select(batch => batch.Id).ToListAsync(cancellationToken), cancellationToken);

        if (missing.Count == 0)
        {
            return [];
        }

        var stock = await store.GetStockAsync(cancellationToken);
        var labels = await db.Containers.AsNoTracking().ToDictionaryAsync(container => container.Id, container => container.LabelCode, cancellationToken);
        var batches = await db.Batches.AsNoTracking()
            .Where(batch => missing.Contains(batch.Id))
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new
            {
                batch.Id,
                batch.TraceabilityCode,
                plant.CommonName,
                plant.Variety,
                batch.Source,
                StartedAt = db.EventLines.Where(line => line.BatchId == batch.Id)
                    .Join(db.Events, line => line.EventId, e => e.Id, (line, e) => e.OccurredAt)
                    .Min(occurredAt => (DateTimeOffset?)occurredAt),
            })
            .ToListAsync(cancellationToken);

        return [.. batches
            .OrderBy(batch => batch.StartedAt)
            .ThenBy(batch => batch.TraceabilityCode, StringComparer.Ordinal)
            .Select(batch => new MissingCost(
                batch.Id,
                batch.TraceabilityCode,
                batch.CommonName,
                batch.Variety,
                batch.Source,
                batch.StartedAt,
                [.. stock.Where(row => row.BatchId == batch.Id).Select(row => labels[row.ContainerId]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]))];
    }

    /// <summary>
    /// Says nobody knows what these batches cost, all in one go or not at all. A batch someone has answered in the
    /// meantime is left as they answered it.
    /// </summary>
    private static async Task<IResult> NotKnownAsync(NotKnownRequest request, ClaimsPrincipal user, HortiplannerDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (request.BatchIds is not { Count: <= MostAtOnce })
        {
            return TypedResults.BadRequest();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ids = request.BatchIds.Distinct().Order().ToList();

        // In one order, so two people doing this at once can't each wait for the other.
        foreach (var id in ids)
        {
            await store.LockRequestAsync(id, cancellationToken);
        }

        var missing = await BatchCostings.MissingAsync(db, ids, cancellationToken);
        var removed = await BatchCostings.Standing(db.BatchCosts.AsNoTracking().Where(cost => missing.Contains(cost.BatchId) && cost.Kind == CostKind.Material))
            .ToDictionaryAsync(cost => cost.BatchId, cost => cost.Id, cancellationToken);
        var person = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = clock.GetUtcNow();

        foreach (var id in missing)
        {
            db.BatchCosts.Add(new BatchCost
            {
                Id = Guid.CreateVersion7(),
                BatchId = id,
                Kind = CostKind.Material,
                Answer = CostAnswer.NotKnown,
                ReplacesId = removed.TryGetValue(id, out var replaces) ? replaces : null,
                OccurredAt = now,
                RecordedBy = person,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(new MarkedNotKnown(missing.Count));
    }

    /// <param name="StartedAt">When its first work was recorded, if any was.</param>
    /// <param name="Trays">The trays and pots it's in now.</param>
    internal sealed record MissingCost(Guid BatchId, string TraceabilityCode, string CommonName, string? Variety, BatchSource Source, DateTimeOffset? StartedAt, IReadOnlyList<string> Trays);

    internal sealed record NotKnownRequest(IReadOnlyList<Guid>? BatchIds);

    internal sealed record MarkedNotKnown(int Marked);
}
