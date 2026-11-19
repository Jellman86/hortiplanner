namespace Hortiplanner.Api.Lookup;

/// <summary>A species' place in the GBIF Backbone Taxonomy, from kingdom down to the lowest rank it reached.</summary>
internal sealed record FoundLineage(IReadOnlyList<FoundLineage.Rung> Rungs)
{
    /// <summary>One rank, such as the family Lamiaceae, with its GBIF key.</summary>
    internal sealed record Rung(string Rank, string Name, long Key);
}
