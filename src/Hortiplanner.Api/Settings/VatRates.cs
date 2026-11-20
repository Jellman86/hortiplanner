using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Settings;

/// <summary>The VAT rate for a band on a day: the newest rate for that band starting on or before it.</summary>
internal static class VatRates
{
    internal static async Task<decimal> PercentAsync(HortiplannerDbContext db, VatBand band, DateOnly day, CancellationToken cancellationToken) =>
        band == VatBand.Zero
            ? 0m
            : await db.VatRateChanges.AsNoTracking()
                .Where(change => change.Band == band && change.From <= day)
                .OrderByDescending(change => change.From)
                .ThenByDescending(change => change.SetAt)
                .Select(change => change.Percent)
                .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The rates an invoice line may carry on a day for a registered nursery: standard, reduced and zero.</summary>
    internal static async Task<HashSet<decimal>> AllowedAsync(HortiplannerDbContext db, DateOnly day, CancellationToken cancellationToken) =>
    [
        await PercentAsync(db, VatBand.Standard, day, cancellationToken),
        await PercentAsync(db, VatBand.Reduced, day, cancellationToken),
        0m,
    ];
}
