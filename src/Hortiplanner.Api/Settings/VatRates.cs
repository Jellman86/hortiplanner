using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Settings;

/// <summary>The VAT rate for a band on a day: the newest rate for that band starting on or before it.</summary>
internal static class VatRates
{
    internal static async Task<decimal> PercentAsync(HortiplannerDbContext db, VatBand band, DateOnly day, CancellationToken cancellationToken)
    {
        if (band == VatBand.Zero)
        {
            return 0m;
        }

        var rates = db.VatRateChanges.AsNoTracking().Where(change => change.Band == band);

        // Two changes on one day: the later wins, and the order they were made in settles a tie.
        return await rates
                .Where(change => change.From <= day)
                .OrderByDescending(change => change.From)
                .ThenByDescending(change => change.SetAt)
                .ThenByDescending(change => change.Id)
                .Select(change => (decimal?)change.Percent)
                .FirstOrDefaultAsync(cancellationToken)
            // A day before the first rate takes the first rate, rather than nothing.
            ?? await rates
                .OrderBy(change => change.From)
                .ThenBy(change => change.SetAt)
                .Select(change => change.Percent)
                .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>The rates an invoice line may carry on a day for a registered nursery: standard, reduced and zero.</summary>
    internal static async Task<HashSet<decimal>> AllowedAsync(HortiplannerDbContext db, DateOnly day, CancellationToken cancellationToken) =>
    [
        await PercentAsync(db, VatBand.Standard, day, cancellationToken),
        await PercentAsync(db, VatBand.Reduced, day, cancellationToken),
        0m,
    ];
}
