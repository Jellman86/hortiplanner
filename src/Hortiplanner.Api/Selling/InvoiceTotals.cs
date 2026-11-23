using Hortiplanner.Api.Costs;
using Hortiplanner.Api.Data;

namespace Hortiplanner.Api.Selling;

/// <summary>An invoice's sums: net, VAT for each rate (rounded per rate, as invoices show it), and the total.</summary>
internal sealed record InvoiceTotals(decimal Net, IReadOnlyList<VatAtRate> Vat, decimal Total)
{
    public static decimal LineNet(InvoiceLine line) => line.Quantity * line.UnitPrice;

    public static InvoiceTotals Of(IEnumerable<InvoiceLine> lines)
    {
        var list = lines.ToList();
        var vat = list
            .Where(line => line.VatRate > 0)
            .GroupBy(line => line.VatRate)
            .OrderByDescending(group => group.Key)
            .Select(group => VatOf(group.Key, group))
            .ToList();

        var totalNet = list.Sum(LineNet);
        return new InvoiceTotals(totalNet, vat, totalNet + vat.Sum(rate => rate.Vat));
    }

    private static VatAtRate VatOf(decimal rate, IEnumerable<InvoiceLine> lines)
    {
        var net = lines.Sum(LineNet);
        return new VatAtRate(rate, net, Math.Round(net * rate / 100m, 2, MidpointRounding.AwayFromZero));
    }

    internal static Dictionary<Guid, decimal> VatByLine(IEnumerable<InvoiceLine> lines)
    {
        var result = new Dictionary<Guid, decimal>();
        foreach (var group in lines.GroupBy(line => line.VatRate))
        {
            var ordered = group.OrderBy(line => line.Position).ThenBy(line => line.Id).ToArray();
            var shares = PennyShares.Allocate(VatOf(group.Key, ordered).Vat, ordered.Select(line => LineNet(line) * group.Key));
            for (var index = 0; index < ordered.Length; index++)
            {
                result.Add(ordered[index].Id, shares[index]);
            }
        }

        return result;
    }
}
