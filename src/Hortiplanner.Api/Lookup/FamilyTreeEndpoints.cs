using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Lookup;

/// <summary>Where a plant sits in the plant kingdom, and the nursery's other plants in the same genus and family.</summary>
internal static class FamilyTreeEndpoints
{
    /// <summary>The ranks whose other members at the nursery are worth listing; everything is in the kingdom.</summary>
    private static readonly HashSet<string> ListedRanks = new(StringComparer.Ordinal) { "order", "family", "genus", "species" };

    private const int DeepestTree = 12;

    public static void MapFamilyTreeEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/plants/{id:guid}/family-tree", GetAsync).Produces<FamilyTree>();

    private static async Task<IResult> GetAsync(Guid id, HortiplannerDbContext db, SpeciesSources sources, IConfiguration configuration, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await db.Plants.AsNoTracking().FirstOrDefaultAsync(plant => plant.Id == id, cancellationToken) is not { } plant)
        {
            return TypedResults.NotFound();
        }

        var species = plant.SpeciesId is { } speciesId ? await db.Species.FirstOrDefaultAsync(entry => entry.Id == speciesId, cancellationToken) : null;

        if (species is null)
        {
            return TypedResults.Ok(new FamilyTree(Linked: false, Unavailable: false, [], [], []));
        }

        var unavailable = await SpeciesLineage.EnsureAsync(species, db, sources, configuration, clock, cancellationToken);
        var taxa = await db.Taxa.AsNoTracking().ToDictionaryAsync(taxon => taxon.Id, cancellationToken);
        var others = await db.Plants.AsNoTracking()
            .Where(other => other.Id != id && other.ArchivedAt == null && other.SpeciesId != null)
            .Join(db.Species.Where(entry => entry.TaxonId != null), other => other.SpeciesId, entry => entry.Id, (other, entry) => new { other.Id, other.CommonName, other.Variety, TaxonId = entry.TaxonId!.Value })
            .ToListAsync(cancellationToken);
        var lineage = Up(species.TaxonId, taxa);
        var within = others.ConvertAll(other => (Plant: new PlantName(other.Id, other.Variety is null ? other.CommonName : $"{other.CommonName} '{other.Variety}'"), Above: Up(other.TaxonId, taxa).Select(taxon => taxon.Id).ToHashSet()));

        List<PlantName> In(Taxon taxon) =>
            [.. within.Where(other => other.Above.Contains(taxon.Id)).Select(other => other.Plant).OrderBy(name => name.Name, StringComparer.OrdinalIgnoreCase)];

        var rungs = lineage.ConvertAll(taxon => new TaxonRung(taxon.Id, taxon.Rank, taxon.ScientificName, taxon.CommonName, ListedRanks.Contains(taxon.Rank) ? In(taxon) : []));
        var genus = rungs.Find(rung => string.Equals(rung.Rank, "genus", StringComparison.Ordinal))?.OtherPlants ?? [];
        var family = rungs.Find(rung => string.Equals(rung.Rank, "family", StringComparison.Ordinal))?.OtherPlants ?? [];

        return TypedResults.Ok(new FamilyTree(
            Linked: true,
            Unavailable: unavailable && lineage.Count == 0,
            rungs,
            genus,
            [.. family.Where(relative => !genus.Contains(relative))]));
    }

    /// <summary>A taxon and everything above it, kingdom first.</summary>
    private static List<Taxon> Up(Guid? from, Dictionary<Guid, Taxon> taxa)
    {
        var path = new List<Taxon>();

        for (var at = from; at is { } current && taxa.TryGetValue(current, out var taxon) && path.Count < DeepestTree; at = taxon.ParentId)
        {
            path.Insert(0, taxon);
        }

        return path;
    }

    /// <param name="Linked">Matched to a species at all; an unmatched plant has no family tree to show.</param>
    /// <param name="Unavailable">The tree isn't saved yet and the online sources couldn't be reached.</param>
    /// <param name="SameFamily">The nursery's plants in the same family, less those in the same genus.</param>
    internal sealed record FamilyTree(bool Linked, bool Unavailable, IReadOnlyList<TaxonRung> Lineage, IReadOnlyList<PlantName> SameGenus, IReadOnlyList<PlantName> SameFamily);

    /// <param name="OtherPlants">The nursery's other plants at this rank, for the ranks worth listing.</param>
    internal sealed record TaxonRung(Guid TaxonId, string Rank, string ScientificName, string? CommonName, IReadOnlyList<PlantName> OtherPlants);

    internal sealed record PlantName(Guid PlantId, string Name);
}
