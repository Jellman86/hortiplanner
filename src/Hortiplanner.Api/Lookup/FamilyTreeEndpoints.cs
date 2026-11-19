using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Lookup;

/// <summary>Where a plant sits in the plant kingdom, and the nursery's other plants in the same order, each placed on its own branch.</summary>
internal static class FamilyTreeEndpoints
{
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
            return TypedResults.Ok(new FamilyTree(Linked: false, Unavailable: false, [], []));
        }

        var unavailable = await SpeciesLineage.EnsureAsync(species, db, sources, configuration, clock, cancellationToken);
        var taxa = await db.Taxa.AsNoTracking().ToDictionaryAsync(taxon => taxon.Id, cancellationToken);
        var lineage = Up(species.TaxonId, taxa);
        var rungs = lineage.ConvertAll(taxon => new TaxonRung(taxon.Id, taxon.Rank, taxon.ScientificName, taxon.CommonName));
        var root = lineage.Find(taxon => Is(taxon, "order")) ?? lineage.Find(taxon => Is(taxon, "family"));
        var relatives = root is null ? [] : await RelativesAsync(id, root.Id, taxa, db, cancellationToken);

        return TypedResults.Ok(new FamilyTree(Linked: true, Unavailable: unavailable && lineage.Count == 0, rungs, relatives));
    }

    /// <summary>The nursery's other plants under the given taxon, each with its own family, genus and species.</summary>
    private static async Task<List<RelatedPlant>> RelativesAsync(Guid id, Guid under, Dictionary<Guid, Taxon> taxa, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var others = await db.Plants.AsNoTracking()
            .Where(other => other.Id != id && other.ArchivedAt == null && other.SpeciesId != null)
            .Join(db.Species.Where(entry => entry.TaxonId != null), other => other.SpeciesId, entry => entry.Id, (other, entry) => new
            {
                other.Id,
                other.CommonName,
                other.Variety,
                TaxonId = entry.TaxonId!.Value,
                HasPhoto = entry.PhotoUrl != null || db.PlantPhotos.Any(photo => photo.PlantId == other.Id),
            })
            .ToListAsync(cancellationToken);

        return [.. others
            .Select(other => (Other: other, Path: Up(other.TaxonId, taxa)))
            .Where(other => other.Path.Exists(taxon => taxon.Id == under))
            .Select(other => new RelatedPlant(
                other.Other.Id,
                other.Other.Variety is null ? other.Other.CommonName : $"{other.Other.CommonName} '{other.Other.Variety}'",
                other.Other.HasPhoto,
                NameAt(other.Path, "family"),
                NameAt(other.Path, "genus"),
                SpeciesOf(other.Path)))
            .OrderBy(other => other.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static bool Is(Taxon taxon, string rank) => string.Equals(taxon.Rank, rank, StringComparison.Ordinal);

    private static TaxonName? NameAt(List<Taxon> path, string rank) =>
        path.Find(taxon => Is(taxon, rank)) is { } found ? new TaxonName(found.Id, found.ScientificName, found.CommonName) : null;

    /// <summary>The species a plant belongs to; a variety or subspecies reports its species.</summary>
    private static TaxonName? SpeciesOf(List<Taxon> path) => NameAt(path, "species");

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
    /// <param name="Relatives">The nursery's other plants in the same order (or family, when there's no order), by name.</param>
    internal sealed record FamilyTree(bool Linked, bool Unavailable, IReadOnlyList<TaxonRung> Lineage, IReadOnlyList<RelatedPlant> Relatives);

    internal sealed record TaxonRung(Guid TaxonId, string Rank, string ScientificName, string? CommonName);

    /// <param name="HasPhoto">Has a photo of its own or a species picture, served at <c>/api/plants/{id}/photo</c>.</param>
    /// <param name="Species">Null when the plant is matched to its genus rather than one species.</param>
    internal sealed record RelatedPlant(Guid PlantId, string Name, bool HasPhoto, TaxonName? Family, TaxonName? Genus, TaxonName? Species);

    internal sealed record TaxonName(Guid TaxonId, string ScientificName, string? CommonName);
}
