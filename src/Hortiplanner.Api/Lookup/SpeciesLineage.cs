using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hortiplanner.Api.Lookup;

/// <summary>A species' family tree, fetched once from GBIF (with everyday names from iNaturalist) and kept here.</summary>
internal static class SpeciesLineage
{
    /// <summary>
    /// Makes sure the species has its family tree. Unavailable means the sources couldn't be reached, so try again later;
    /// a species GBIF doesn't match exactly simply has none.
    /// </summary>
    public static async Task<bool> EnsureAsync(Species species, HortiplannerDbContext db, SpeciesSources sources, IConfiguration configuration, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        if (species.TaxonId is not null
            || (species.LineageCheckedAt is { } checkedAt && now - checkedAt < AskAgainAfter)
            || !configuration.GetValue("Species:Online", defaultValue: true))
        {
            return false;
        }

        var name = species.AcceptedName ?? species.ScientificName;
        var (lineage, reached) = await sources.MatchLineageAsync(name, cancellationToken);

        if (lineage is null)
        {
            // Not known to GBIF: remembered, so it isn't asked on every look. Not reached: tried again next time.
            if (reached)
            {
                species.LineageCheckedAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }

            return !reached;
        }

        // Without iNaturalist the everyday names would be lost for good, so the whole tree waits for it.
        var commonNames = species.InaturalistId is { } taxon ? await sources.FetchCommonNamesAsync(taxon, cancellationToken) : [];

        if (commonNames is null)
        {
            return true;
        }

        species.TaxonId = (await SaveRungsAsync(lineage, commonNames, db, now, cancellationToken)).Id;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request saved the same rungs a moment ago. Nothing of this attempt stands, so it is tried again.
            db.ChangeTracker.Clear();
            species.TaxonId = null;
            return true;
        }

        return false;
    }

    /// <summary>Each rung of the tree, kept or added, with any everyday names; returns the lowest.</summary>
    private static async Task<Taxon> SaveRungsAsync(FoundLineage lineage, Dictionary<(string Rank, string Name), string> commonNames, HortiplannerDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var keys = lineage.Rungs.Select(rung => rung.Key).ToList();
        var known = await db.Taxa.Where(item => keys.Contains(item.GbifKey)).ToDictionaryAsync(item => item.GbifKey, cancellationToken);
        Taxon? parent = null;

        foreach (var rung in lineage.Rungs)
        {
            if (!known.TryGetValue(rung.Key, out var row))
            {
                // Only a new rung takes its parent from this tree: a shared one keeps the parent it was saved with, so a
                // species GBIF gives with a rank missing can't re-hang a family every relative shares.
                row = new Taxon { Rank = rung.Rank, ScientificName = rung.Name, GbifKey = rung.Key, ParentId = parent?.Id, Source = SpeciesSources.GbifName, Licence = SpeciesSources.GbifLicence, FetchedAt = now };
                db.Taxa.Add(row);
                known[rung.Key] = row;
            }

            if (row.CommonName is null && commonNames.TryGetValue((rung.Rank, rung.Name), out var common))
            {
                row.CommonName = common;
                row.CommonNameSource = SpeciesSources.INaturalistName;
            }

            parent = row;
        }

        return parent!;
    }

    private static readonly TimeSpan AskAgainAfter = TimeSpan.FromDays(30);
}
