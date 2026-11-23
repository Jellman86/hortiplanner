namespace Hortiplanner.Api.Costs;

internal static class PennyShares
{
    /// <summary>Gives the spare pennies to the largest fractional shares, using input order to break ties.</summary>
    internal static decimal[] Allocate(decimal total, IEnumerable<decimal> exactPennies)
    {
        var exact = exactPennies.ToArray();
        var whole = exact.Select(decimal.Floor).ToArray();
        var spare = (int)((total * 100m) - whole.Sum());

        foreach (var index in Enumerable.Range(0, whole.Length).OrderByDescending(index => exact[index] - whole[index]).Take(spare))
        {
            whole[index]++;
        }

        return Array.ConvertAll(whole, pennies => pennies / 100m);
    }
}
