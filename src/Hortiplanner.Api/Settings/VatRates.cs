using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Settings;

/// <summary>The VAT rates for a day, read together so every band comes from the same database snapshot.</summary>
internal static class VatRates
{
    private static IQueryable<VatRateChange> On(HortiplannerDbContext db, VatBand band, DateOnly day) =>
        db.VatRateChanges.AsNoTracking().Where(change => change.Band == band)
            // The latest effective rate wins. Before the first rate, use the earliest one.
            .OrderByDescending(change => change.From <= day)
            .ThenByDescending(change => change.From <= day ? (DateOnly?)change.From : null)
            .ThenBy(change => change.From)
            .ThenByDescending(change => change.From <= day ? (DateTimeOffset?)change.SetAt : null)
            .ThenBy(change => change.SetAt)
            .ThenByDescending(change => change.Id)
            .Take(1);

    internal static Task<decimal> PercentAsync(HortiplannerDbContext db, VatBand band, DateOnly day, CancellationToken cancellationToken) =>
        band == VatBand.Zero
            ? Task.FromResult(0m)
            : On(db, band, day).Select(change => change.Percent).FirstOrDefaultAsync(cancellationToken);

    internal static async Task<(decimal Standard, decimal Reduced)> PairAsync(HortiplannerDbContext db, DateOnly day, CancellationToken cancellationToken)
    {
        var rows = await On(db, VatBand.Standard, day).Concat(On(db, VatBand.Reduced, day))
            .Select(change => new { change.Band, change.Percent })
            .ToDictionaryAsync(change => change.Band, change => change.Percent, cancellationToken);
        return (rows.GetValueOrDefault(VatBand.Standard), rows.GetValueOrDefault(VatBand.Reduced));
    }
}
