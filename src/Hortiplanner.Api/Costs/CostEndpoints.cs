using System.Security.Claims;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Costs;

/// <summary>
/// What a batch cost. Anyone can say what its seed or plants cost, from a tray, and a phone without a signal sends it
/// later. Only the owner sees costs afterwards, says a cost isn't known, removes one or says how much was VAT.
/// </summary>
internal static class CostEndpoints
{
    private const decimal MostPaid = 99_999.99m;
    private const int LongestNote = 200;

    /// <summary>Work dated this far ahead of the server is taken as happening now, as other work is.</summary>
    private static readonly TimeSpan ClockAllowance = TimeSpan.FromMinutes(5);

    public static void MapCostEndpoints(this IEndpointRouteBuilder app)
    {
        // Accepted while Costs is turned off, so a cost waiting on a phone in good faith isn't lost.
        app.MapPost("/api/batches/{id:guid}/costs", AddAsync)
            .AddEndpointFilter(RequestFingerprint.Capture)
            .Produces<CostRecorded>(StatusCodes.Status201Created)
            .Produces<CostRecorded>()
            .Produces<CostRefused>(StatusCodes.Status409Conflict);

        app.MapGet("/api/batches/{id:guid}/costing", BatchCostings.GetAsync)
            .RequireAuthorization(AccountServices.OwnerPolicy)
            .RequireFeature(NurseryFeature.Costs)
            .Produces<BatchCostings.BatchCosting>();
    }

    private static async Task<IResult> AddAsync(
        Guid id,
        CostRequest request,
        ClaimsPrincipal user,
        HortiplannerDbContext db,
        EventStore store,
        DeviceClock device,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRequestAsync(request.EventId, cancellationToken);
        var person = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Checked first: a resend is not a new answer, whatever has been said since.
        if (await db.BatchCosts.AsNoTracking().FirstOrDefaultAsync(cost => cost.Id == request.EventId, cancellationToken) is { } stored)
        {
            return stored.RecordedBy == person && stored.BatchId == id && string.Equals(stored.RequestFingerprint, device.RequestFingerprint, StringComparison.Ordinal)
                ? TypedResults.Ok(new CostRecorded(stored.Id))
                : TypedResults.Conflict(new CostRefused("IdInUse"));
        }

        if (!IsValid(request))
        {
            return TypedResults.BadRequest();
        }

        if (!AccountServices.IsOwner(user) && IsOwnersOnly(request))
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (await db.Batches.AsNoTracking().Where(batch => batch.Id == id).Select(batch => new { batch.PlantId }).FirstOrDefaultAsync(cancellationToken) is not { } batch)
        {
            return TypedResults.NotFound();
        }

        // One answer at a time for each batch, so two people answering at once leaves one standing.
        await store.LockRequestAsync(id, cancellationToken);
        var replaces = await ReplacesAsync(id, request, db, cancellationToken);

        if (replaces is { Refused: { } refused })
        {
            return TypedResults.Conflict(new CostRefused(refused));
        }

        // A correction keeps the day the cost was for, and so that day's VAT rate.
        var occurredAt = replaces.Replaced is { Answer: CostAnswer.Amount } amount ? amount.OccurredAt : OnServerClock(request.OccurredAt, device, clock);
        var cost = await BuildAsync(id, batch.PlantId, request, person, occurredAt, replaces.Replaced?.Id, device, db, clock, cancellationToken);
        db.BatchCosts.Add(cost);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/batches/{id}/costs/{cost.Id}", new CostRecorded(cost.Id));
    }

    private static bool IsValid(CostRequest request) =>
        request.EventId != Guid.Empty
        && Enum.IsDefined(request.Kind)
        && Enum.IsDefined(request.Answer)
        && (request.VatBand is null || Enum.IsDefined(request.VatBand.Value))
        && (request.Answer == CostAnswer.Amount ? request.Amount is > 0 and <= MostPaid && decimal.Round(request.Amount.Value, 2) == request.Amount : request.Amount is null)
        && (request.VatBand is null) == (request.IncludesVat is null)
        && (request.VatBand is null || request.Answer == CostAnswer.Amount)
        && (request.Kind == CostKind.Material || request.Answer is CostAnswer.Amount or CostAnswer.Removed)
        && (request.Kind == CostKind.Material || request.Answer == CostAnswer.Removed || Clean(request.Note) is not null)
        && (request.Answer != CostAnswer.Removed || request.ReplacesId is not null)
        && (request.Note is null || request.Note.Trim().Length <= LongestNote);

    /// <summary>Saying a cost isn't known, removing or changing one, adding anything else, or saying what VAT was in it.</summary>
    private static bool IsOwnersOnly(CostRequest request) =>
        request.Answer is CostAnswer.NotKnown or CostAnswer.Removed
        || request.VatBand is not null
        || request.Kind == CostKind.Other
        || request.ReplacesId is not null;

    /// <summary>
    /// The entry this one takes the place of, which must still stand, so nobody's answer is overwritten by one given
    /// without seeing it, such as a cost that waited days on a phone. Without one, the seed or plants can only be
    /// answered while nothing stands for them.
    /// </summary>
    private static async Task<(BatchCost? Replaced, string? Refused)> ReplacesAsync(Guid batchId, CostRequest request, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var standing = await BatchCostings.Standing(db.BatchCosts.AsNoTracking().Where(cost => cost.BatchId == batchId && cost.Kind == request.Kind))
            .Where(cost => cost.Answer != CostAnswer.Removed)
            .ToListAsync(cancellationToken);

        if (request.ReplacesId is { } replaces)
        {
            return standing.Find(cost => cost.Id == replaces) is { } replaced ? (replaced, null) : (null, "Replaced");
        }

        if (request.Kind == CostKind.Other)
        {
            return (null, null);
        }

        // The removed answer that stands, if any, is what a new one follows on from.
        var removed = await BatchCostings.Standing(db.BatchCosts.AsNoTracking().Where(cost => cost.BatchId == batchId && cost.Kind == CostKind.Material))
            .FirstOrDefaultAsync(cancellationToken);

        return standing.Count > 0 ? (null, "Answered") : (removed, null);
    }

    private static async Task<BatchCost> BuildAsync(
        Guid batchId,
        Guid plantId,
        CostRequest request,
        Guid person,
        DateTimeOffset occurredAt,
        Guid? replacesId,
        DeviceClock device,
        HortiplannerDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var vat = request.Answer == CostAnswer.Amount
            ? await SplitAsync(request, plantId, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(occurredAt, clock.LocalTimeZone).DateTime), db, cancellationToken)
            : null;

        return new BatchCost
        {
            Id = request.EventId,
            BatchId = batchId,
            Kind = request.Kind,
            Answer = request.Answer,
            AmountPaid = request.Answer == CostAnswer.Amount ? request.Amount : null,
            IncludesVat = vat?.IncludesVat,
            VatBand = vat?.Band,
            VatRate = vat?.Rate,
            VatSaid = request.VatBand is not null,
            NetCost = vat?.Net,
            Vat = vat?.Vat,
            Note = request.Kind == CostKind.Other ? Clean(request.Note) : null,
            ReplacesId = replacesId,
            OccurredAt = occurredAt,
            RecordedBy = person,
            RequestFingerprint = device.RequestFingerprint,
        };
    }

    /// <summary>
    /// Nobody is asked about VAT when typing a cost, so unless the owner says otherwise the amount is taken as what was
    /// paid, VAT included, at the plant's own rate for its seed or plants and the standard rate for anything else.
    /// </summary>
    private static async Task<VatSplit> SplitAsync(CostRequest request, Guid plantId, DateOnly day, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var band = request.VatBand
            ?? (request.Kind == CostKind.Material
                ? await db.Plants.Where(plant => plant.Id == plantId).Select(plant => plant.VatBand).SingleAsync(cancellationToken)
                : VatBand.Standard);
        var rate = await VatRates.PercentAsync(db, band, day, cancellationToken);
        var includesVat = request.IncludesVat ?? true;
        var (net, vat) = Split(request.Amount!.Value, rate, includesVat);
        return new VatSplit(band, rate, includesVat, net, vat);
    }

    /// <summary>To the penny, rounding a half up, as invoices do.</summary>
    internal static (decimal Net, decimal Vat) Split(decimal amount, decimal rate, bool includesVat)
    {
        if (includesVat)
        {
            var net = Math.Round(amount * 100m / (100m + rate), 2, MidpointRounding.AwayFromZero);
            return (net, amount - net);
        }

        return (amount, Math.Round(amount * rate / 100m, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>A cost timed by a device whose clock is out is put back on the server's clock, and never in the future.</summary>
    private static DateTimeOffset OnServerClock(DateTimeOffset occurredAt, DeviceClock device, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        var adjusted = device.Skew is { } skew && skew.Duration() > DeviceClock.Tolerance ? occurredAt + skew : occurredAt;
        return adjusted > now + ClockAllowance ? now : adjusted.ToUniversalTime();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record VatSplit(VatBand Band, decimal Rate, bool IncludesVat, decimal Net, decimal Vat);

    internal sealed record CostRequest(
        Guid EventId,
        DateTimeOffset OccurredAt,
        CostKind Kind,
        CostAnswer Answer,
        decimal? Amount = null,
        VatBand? VatBand = null,
        bool? IncludesVat = null,
        string? Note = null,
        Guid? ReplacesId = null);

    internal sealed record CostRecorded(Guid EventId);

    /// <param name="Problem">IdInUse, Replaced (what it replaces no longer stands) or Answered (someone has already said).</param>
    internal sealed record CostRefused(string Problem);
}
