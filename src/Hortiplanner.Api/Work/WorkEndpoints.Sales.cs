using System.Security.Claims;
using System.Text.Json;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Work;

/// <summary>
/// Plants sold from a tray, recorded by hand: how many, and the price for each if known. It's work like any other,
/// so it waits on a phone without a signal and can be undone. It keeps the VAT it was charged at, which is none while
/// the nursery isn't registered.
/// </summary>
internal static partial class WorkEndpoints
{
    private const decimal MostPriceEach = 9_999.99m;

    /// <summary>Whether a sale was recorded from a tray, rather than by an invoice or, later, a till.</summary>
    internal static bool IsTraySale(string payload)
    {
        using var details = JsonDocument.Parse(payload);
        return details.RootElement.TryGetProperty("sale", out var sale) && sale.ValueKind == JsonValueKind.String && string.Equals(sale.GetString(), "tray", StringComparison.Ordinal);
    }

    private static async Task<IResult> RecordSaleAsync(Guid id, SaleRequest request, ClaimsPrincipal user, HortiplannerDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRequestAsync(request.EventId, cancellationToken);
        await store.LockAsync([id], cancellationToken);
        if (await store.MatchRequestAsync(request.EventId, UserId(user), cancellationToken) is { } repeated)
        {
            return repeated == AppendResult.AlreadyStored ? TypedResults.Ok(new WorkRecorded(request.EventId)) : Refused("IdInUse");
        }

        if (!IsValidQuantity(request.Quantity) || request.PriceEach is < 0 or > MostPriceEach || (request.PriceEach is { } price && decimal.Round(price, 2) != price))
        {
            return TypedResults.BadRequest();
        }

        // Nobody knows how many are in a tray not counted yet, so it's counted before a sale is taken from it.
        if (await store.IsNotCountedAsync(id, request.BatchId, cancellationToken))
        {
            return Refused("NotCounted");
        }

        var vatRate = await SaleVatRateAsync(request.BatchId, request.OccurredAt, db, clock, cancellationToken);
        var work = NewEvent(request.EventId, EventType.Sold, request.OccurredAt, user, id, details: new { sale = "tray", priceEach = request.PriceEach, vatRate });
        AddLine(work, id, request.BatchId, -request.Quantity);
        var result = await StoreSimpleAsync(store, work, cancellationToken);

        if (result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK or StatusCodes.Status201Created })
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    /// <summary>None while the nursery isn't registered; once it is, the plant's own rate on the day of the sale.</summary>
    private static async Task<decimal> SaleVatRateAsync(Guid batchId, DateTimeOffset occurredAt, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!(await db.GetNurserySettingsAsync(cancellationToken)).VatRegistered)
        {
            return 0m;
        }

        var band = await db.Batches.Where(batch => batch.Id == batchId).Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => (VatBand?)plant.VatBand).FirstOrDefaultAsync(cancellationToken);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(occurredAt, clock.LocalTimeZone).DateTime);
        return band is { } known ? await VatRates.PercentAsync(db, known, day, cancellationToken) : 0m;
    }

    /// <param name="PriceEach">What one sold for, VAT included, if known.</param>
    internal sealed record SaleRequest(Guid EventId, DateTimeOffset OccurredAt, Guid BatchId, int Quantity, decimal? PriceEach = null);
}
