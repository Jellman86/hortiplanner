namespace Hortiplanner.Api.Data;

/// <summary>
/// One rung of the plant kingdom's family tree, from kingdom down to species, as the GBIF Backbone Taxonomy has it.
/// Each points at the rung above, and is shared by every species beneath it, so Lamiaceae is one row for every mint.
/// Kept here so a plant's family tree shows without the outside service.
/// </summary>
internal sealed class Taxon
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>kingdom, phylum, class, order, family, genus or species.</summary>
    public required string Rank { get; init; }

    public required string ScientificName { get; set; }

    /// <summary>An everyday name, such as "Mint Family", from iNaturalist when it has one.</summary>
    public string? CommonName { get; set; }

    public string? CommonNameSource { get; set; }

    public Guid? ParentId { get; set; }

    public required long GbifKey { get; init; }

    public required string Source { get; init; }

    public required string Licence { get; init; }

    public required DateTimeOffset FetchedAt { get; set; }
}
