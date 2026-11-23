using Hortiplanner.Api.Data;
using Hortiplanner.Api.Selling;
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
    internal static async Task<List<Sale>> ForBatchesAsync(
        IReadOnlyCollection<Guid> batchIds, HortiplannerDbContext db, (DateOnly From, DateOnly To)? unbatchedWindow,
        CancellationToken cancellationToken)
    {
        var invoices = db.Invoices.AsNoTracking()
            .Where(invoice => invoice.Kind == InvoiceKind.Invoice && !db.Invoices.Any(credit => credit.CreditsInvoiceId == invoice.Id));

        invoices = unbatchedWindow is { } window
            ? invoices.Where(invoice => invoice.Lines.Any(line => line.BatchId != null && batchIds.Contains(line.BatchId.Value))
                || (invoice.IssuedOn >= window.From && invoice.IssuedOn <= window.To && invoice.Lines.Any(line => line.BatchId == null && line.PlantId != null)))
            : invoices.Where(invoice => invoice.Lines.Any(line => line.BatchId != null && batchIds.Contains(line.BatchId.Value)));

        // A line shares in the rounding of its whole invoice, including any lines outside this report.
        var invoiced = await invoices.Select(invoice => new { invoice.IssuedOn, Lines = invoice.Lines.ToList() }).ToListAsync(cancellationToken);
        var selected = batchIds.ToHashSet();
        var sales = new List<Sale>();
        foreach (var invoice in invoiced)
        {
            var vat = InvoiceTotals.VatByLine(invoice.Lines);
            foreach (var line in invoice.Lines)
            {
                var included = line.BatchId is { } batchId ? selected.Contains(batchId)
                    : line.PlantId is not null && unbatchedWindow is { } dates && invoice.IssuedOn >= dates.From && invoice.IssuedOn <= dates.To;
                if (included)
                {
                    var net = InvoiceTotals.LineNet(line);
                    sales.Add(new Sale(line.PlantId, line.BatchId, line.Quantity, new MoneyPair(net + vat[line.Id], net), SaleSource.Invoice));
                }
            }
        }

        var fromTrays = await db.EventLines.AsNoTracking()
            .Where(line => batchIds.Contains(line.BatchId) && line.QuantityChange < 0)
            .Join(db.Events.Where(e => e.Type == EventType.Sold && !db.Events.Any(correction => correction.CorrectsEventId == e.Id)), line => line.EventId, e => e.Id, (line, e) => new { line.BatchId, line.QuantityChange, e.Payload })
            .ToListAsync(cancellationToken);
        sales.AddRange(fromTrays.Where(row => WorkEndpoints.IsTraySale(row.Payload))
            .Select(row => new Sale(null, row.BatchId, -row.QuantityChange, TraySaleTakings(row.Payload, -row.QuantityChange), SaleSource.Tray)));

        return sales;
    }

    /// <summary>What a sale from a tray took, with VAT and before VAT, or null when no price was recorded.</summary>
    internal static MoneyPair? TraySaleTakings(string payload, int quantity)
    {
        var (priceEach, vatRate) = TraySale(payload);
        return priceEach is { } price ? new MoneyPair(quantity * price, quantity * price / (1 + (vatRate / 100m))) : null;
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
    internal sealed record Sale(Guid? PlantId, Guid? BatchId, int Quantity, MoneyPair? Takings, SaleSource Source);
}
