using System.Security.Claims;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Settings;

/// <summary>
/// The VAT rates, set under Settings, Nursery details. Separate from the nursery's details, so an older app saving
/// those can't send the rates back as nothing. A change starts today and never rewrites anything already recorded.
/// </summary>
internal static class VatRateEndpoints
{
    public static void MapVatRateEndpoints(this IEndpointRouteBuilder app)
    {
        var rates = app.MapGroup("/api/settings/vat-rates").RequireAuthorization(AccountServices.OwnerPolicy);
        rates.MapGet("/", GetAsync);
        rates.MapPut("/", SaveAsync).Produces<VatRatesRefused>(StatusCodes.Status400BadRequest);
    }

    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private static async Task<VatRatesSummary> GetAsync(HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var today = Today(clock);
        var changes = await db.VatRateChanges.AsNoTracking()
            .OrderBy(change => change.From)
            .ThenBy(change => change.SetAt)
            .Select(change => new VatRateHistory(
                change.Band,
                change.Percent,
                change.From,
                change.SetBy == null ? null : db.Users.Where(person => person.Id == change.SetBy).Select(person => person.UserName).FirstOrDefault(),
                change.SetAt))
            .ToListAsync(cancellationToken);

        var (standard, reduced) = await VatRates.PairAsync(db, today, cancellationToken);
        return new VatRatesSummary(standard, reduced, changes);
    }

    private static bool IsRate(decimal percent) => percent is >= 0 and <= 100 && decimal.Round(percent, 2) == percent;

    private static async Task<IResult> SaveAsync(VatRatesRequest request, ClaimsPrincipal user, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        // Both rates every time: a missing one is refused, never taken as nought.
        if (request.Standard is not { } standard || request.Reduced is not { } reduced || !IsRate(standard) || !IsRate(reduced))
        {
            return TypedResults.BadRequest(new VatRatesRefused("Rate"));
        }

        if (reduced > standard)
        {
            return TypedResults.BadRequest(new VatRatesRefused("ReducedAboveStandard"));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE vat_rate_changes IN SHARE ROW EXCLUSIVE MODE", cancellationToken);

        var today = Today(clock);
        var current = await VatRates.PairAsync(db, today, cancellationToken);
        var last = await db.VatRateChanges.Where(change => change.From == today)
            .MaxAsync(change => (DateTimeOffset?)change.SetAt, cancellationToken);
        var setAt = clock.GetUtcNow();
        // PostgreSQL stores microseconds. Saves on the same day must sort in lock order, even on a fixed clock.
        if (last is { } previous && setAt < previous.AddTicks(10))
        {
            setAt = previous.AddTicks(10);
        }
        var setBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var changed = new List<VatBand>();

        foreach (var (band, percent, before) in new[] { (VatBand.Standard, standard, current.Standard), (VatBand.Reduced, reduced, current.Reduced) })
        {
            if (before != percent)
            {
                db.VatRateChanges.Add(new VatRateChange { Band = band, Percent = percent, From = today, SetBy = setBy, SetAt = setAt });
                changed.Add(band);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(new VatRatesSaved(changed));
    }

    internal sealed record VatRatesRequest(decimal? Standard, decimal? Reduced);

    /// <param name="Changed">Which rates changed, so the screen can say so: none when they were already in use.</param>
    internal sealed record VatRatesSaved(IReadOnlyList<VatBand> Changed);

    /// <param name="Problem">"Rate" (not a percentage from 0 to 100) or "ReducedAboveStandard".</param>
    internal sealed record VatRatesRefused(string Problem);

    /// <param name="SetBy">Who changed it; nobody for the starting rates.</param>
    internal sealed record VatRateHistory(VatBand Band, decimal Percent, DateOnly From, string? SetBy, DateTimeOffset SetAt);

    /// <param name="Standard">The standard rate in use today.</param>
    /// <param name="Reduced">The reduced rate in use today.</param>
    /// <param name="Changes">Every rate, oldest first, with who set it and from when.</param>
    internal sealed record VatRatesSummary(decimal Standard, decimal Reduced, IReadOnlyList<VatRateHistory> Changes);
}
