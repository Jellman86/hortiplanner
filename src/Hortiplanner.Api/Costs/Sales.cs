using Hortiplanner.Api.Data;
using Hortiplanner.Api.Work;
using Microsoft.EntityFrameworkCore;
using MoneyPair = Hortiplanner.Api.Costs.BatchCostings.MoneyPair;

namespace Hortiplanner.Api.Costs;

/// <summary>
/// Every sale, read in one place: what plant, which batch if known, how many, what it took with VAT and before VAT,
/// and where it was recorded. Invoices not cancelled by a credit note, and sales recorded from a tray that weren't
/// undone. A sale with no price adds to the number sold but not to the takings.
/// </summary>
internal static class Sales
{
    internal static async Task<List<Sale>> ForBatchAsync(Guid batchId, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var invoiced = await db.Invoices.AsNoTracking()
            .Where(invoice => invoice.Kind == InvoiceKind.Invoice && !db.Invoices.Any(credit => credit.CreditsInvoiceId == invoice.Id))
            .SelectMany(invoice => invoice.Lines.Where(line => line.BatchId == batchId), (invoice, line) => new { line.PlantId, line.Quantity, line.UnitPrice, line.VatRate })
            .ToListAsync(cancellationToken);

        var fromTrays = await db.EventLines.AsNoTracking()
            .Where(line => line.BatchId == batchId && line.QuantityChange < 0)
            .Join(db.Events.Where(e => e.Type == EventType.Sold && !db.Events.Any(correction => correction.CorrectsEventId == e.Id)), line => line.EventId, e => e.Id, (line, e) => new { line.QuantityChange, e.Payload })
            .ToListAsync(cancellationToken);
        var plantId = await db.Batches.Where(batch => batch.Id == batchId).Select(batch => batch.PlantId).FirstOrDefaultAsync(cancellationToken);

        return
        [
            .. invoiced.Select(line =>
            {
                // An invoice line's price is before VAT, at the rate it carries (none while not registered).
                var before = line.Quantity * line.UnitPrice;
                return new Sale(line.PlantId ?? plantId, batchId, line.Quantity, new MoneyPair(before * (1 + (line.VatRate / 100m)), before), SaleSource.Invoice);
            }),
            .. fromTrays.Where(row => WorkEndpoints.IsTraySale(row.Payload)).Select(row =>
            {
                var (priceEach, vatRate) = TraySale(row.Payload);
                var quantity = -row.QuantityChange;
                var takings = priceEach is { } price ? new MoneyPair(quantity * price, quantity * price / (1 + (vatRate / 100m))) : null;
                return new Sale(plantId, batchId, quantity, takings, SaleSource.Tray);
            }),
        ];
    }

    /// <summary>The price for each, VAT included, and the VAT rate a sale from a tray was charged at.</summary>
    private static (decimal? PriceEach, decimal VatRate) TraySale(string payload)
    {
        using var details = System.Text.Json.JsonDocument.Parse(payload);
        var root = details.RootElement;
        decimal? price = root.TryGetProperty("priceEach", out var each) && each.ValueKind == System.Text.Json.JsonValueKind.Number ? each.GetDecimal() : null;
        var rate = root.TryGetProperty("vatRate", out var vat) && vat.ValueKind == System.Text.Json.JsonValueKind.Number ? vat.GetDecimal() : 0m;
        return (price, rate);
    }

    /// <param name="Takings">With VAT and before VAT, or null when no price was recorded.</param>
    internal sealed record Sale(Guid PlantId, Guid? BatchId, int Quantity, MoneyPair? Takings, SaleSource Source);
}
