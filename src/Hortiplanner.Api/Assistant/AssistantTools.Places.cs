using System.Globalization;
using System.Text.Json.Nodes;
using Hortiplanner.Api.Climate;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Production;
using Hortiplanner.Api.Reminders;
using Hortiplanner.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Assistant;

/// <summary>One place and one plant, looked up by id: what a person asking from a place's or a plant's page means.</summary>
internal static partial class AssistantTools
{
    private const int RecentDays = 30;

    /// <summary>
    /// A place with everything growing in it and in the places inside it, its climate and jobs while those are on,
    /// and the last month's losses and treatments on trays standing there now. Never who did the work, and never
    /// where the place is on the map.
    /// </summary>
    internal static async Task<object> PlaceAsync(Guid id, ToolContext context)
    {
        var places = await context.Db.Locations.AsNoTracking()
            .Select(place => new { place.Id, place.Name, place.Kind, place.ParentId, place.LabelCode, place.ArchivedAt })
            .ToListAsync(context.Cancellation);

        if (places.Find(place => place.Id == id) is not { } here)
        {
            return new { problem = "There's no such place." };
        }

        var byId = places.ToDictionary(place => place.Id);
        var within = new HashSet<Guid> { id };

        // Everything inside it, however deep, each place once.
        for (var added = true; added;)
        {
            added = false;

            foreach (var place in places.Where(place => place.ParentId is { } parent && within.Contains(parent) && !within.Contains(place.Id)))
            {
                within.Add(place.Id);
                added = true;
            }
        }

        var path = new List<string> { here.Name };
        for (var at = here.ParentId; at is { } parentId && byId.TryGetValue(parentId, out var parent) && path.Count < 12; at = parent.ParentId)
        {
            path.Insert(0, parent.Name);
        }

        context.Saw("place", id.ToString(), here.Name);
        var rows = (await ProductionEndpoints.ListAsync(context.Db, context.Store, context.Cancellation))
            .Where(row => row.LocationId is { } at && within.Contains(at))
            .ToList();
        var trays = await TraysLastInAsync(within, context);

        return new
        {
            place = here.Name,
            kind = here.Kind.ToString(),
            label = here.LabelCode,
            within = string.Join(", ", path),
            noLongerUsed = here.ArchivedAt is not null,
            placesInside = places
                .Where(place => place.ParentId == id && place.ArchivedAt == null)
                .OrderBy(place => place.Name, StringComparer.OrdinalIgnoreCase)
                .Select(place => new { name = place.Name, kind = place.Kind.ToString() }),
            growing = StockRows(rows, context, note: false),
            climate = context.FeaturesOff.Contains(NurseryFeature.Climate) ? null : await PlaceClimateAsync(within, id, context),
            jobs = context.FeaturesOff.Contains(NurseryFeature.Jobs)
                ? null
                : (await JobEndpoints.ListAsync(context.Db, context.Clock, context.Cancellation))
                    .Where(job => job.LocationId is { } at && within.Contains(at))
                    .Take(MostRows)
                    .Select(job => new { job.Title, job.Notes, job.EveryDays, nextDue = job.NextDue.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-GB")), place = job.LocationName, job.Due, job.DaysLate }),
            recentLosses = await RecentWorkAsync(EventType.Lost, trays, context, payload => new { reason = Field(payload, "reason") }),
            recentTreatments = await RecentWorkAsync(EventType.Treated, trays, context, payload => new { product = Field(payload, "product"), dose = Field(payload, "dose"), area = Field(payload, "area") }),
        };
    }

    /// <summary>
    /// Every tray whose last place is here or inside, including trays since lost entirely, so a question about what was
    /// lost here sees the worst of it.
    /// </summary>
    private static async Task<HashSet<Guid>> TraysLastInAsync(HashSet<Guid> within, ToolContext context)
    {
        var ever = await context.Db.Events.AsNoTracking()
            .Where(item => item.ContainerId != null && item.LocationId != null && within.Contains(item.LocationId.Value))
            .Select(item => item.ContainerId!.Value)
            .Distinct()
            .ToListAsync(context.Cancellation);
        var placed = await context.Store.GetPlacementsAsync(ever, context.Cancellation);

        return [.. placed.Where(entry => entry.Value.LocationId is { } at && within.Contains(at)).Select(entry => entry.Key)];
    }

    /// <summary>The latest reading and today's range for the place and each place inside it, and a week of the place's own days.</summary>
    private static async Task<object> PlaceClimateAsync(HashSet<Guid> within, Guid id, ToolContext context) => new
    {
        now = (await ClimateEndpoints.ListAsync(context.Db, context.Clock, context.Cancellation))
            .Where(item => within.Contains(item.LocationId))
            .Take(MostRows)
            .Select(item => new { place = item.Place, latest = item.Latest, today = item.Today }),
        // Only the place's own sensor: places inside it with sensors of their own are in "now".
        lastWeekHere = await ClimateAsync(id, 7, context),
    };

    private static async Task<object> RecentWorkAsync(EventType type, HashSet<Guid> trays, ToolContext context, Func<string, object> describe)
    {
        var since = context.Clock.GetUtcNow().AddDays(-RecentDays);
        var work = await context.Db.Events.AsNoTracking()
            .Where(item => item.Type == type
                && item.ContainerId != null
                && trays.Contains(item.ContainerId.Value)
                && item.OccurredAt >= since
                && !context.Db.Events.Any(undo => undo.CorrectsEventId == item.Id))
            .OrderByDescending(item => item.OccurredAt)
            .Take(MostRows)
            .Select(item => new
            {
                item.OccurredAt,
                item.Payload,
                Tray = context.Db.Containers.Where(tray => tray.Id == item.ContainerId).Select(tray => tray.LabelCode).FirstOrDefault(),
                Change = item.Lines.Sum(line => line.QuantityChange),
            })
            .ToListAsync(context.Cancellation);

        return work.Select(item => new
        {
            when = item.OccurredAt.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-GB")),
            tray = item.Tray,
            change = item.Change,
            details = describe(item.Payload),
        });
    }

    /// <summary>One of the nursery's plants by id, with its care notes and how many are growing.</summary>
    internal static async Task<object> PlantAsync(Guid id, ToolContext context)
    {
        if (await context.Db.Plants.AsNoTracking().FirstOrDefaultAsync(plant => plant.Id == id, context.Cancellation) is not { } plant)
        {
            return new { problem = "There's no such plant." };
        }

        context.Saw("plant", plant.Id.ToString(), PlantName(plant.CommonName, plant.Variety));
        var rows = (await ProductionEndpoints.ListAsync(context.Db, context.Store, context.Cancellation)).Where(row => row.PlantId == id).ToList();
        var (family, genus) = await FamilyAndGenusAsync(plant.SpeciesId, context);

        return new
        {
            id = plant.Id,
            name = PlantName(plant.CommonName, plant.Variety),
            botanicalName = plant.BotanicalName,
            plant.Category,
            plant.Position,
            plant.Watering,
            plant.Hardiness,
            xylellaHost = plant.XylellaHost,
            noLongerGrown = plant.ArchivedAt is not null,
            family,
            genus,
            growing = StockRows(rows, context, note: false),
        };
    }

    /// <summary>The plant's family and genus from its kept family tree, if it has one; never an outside call.</summary>
    private static async Task<(object? Family, object? Genus)> FamilyAndGenusAsync(Guid? speciesId, ToolContext context)
    {
        var at = speciesId is { } id ? await context.Db.Species.Where(species => species.Id == id).Select(species => species.TaxonId).FirstOrDefaultAsync(context.Cancellation) : null;
        object? family = null;
        object? genus = null;

        for (var steps = 0; at is { } current && steps < 12; steps++)
        {
            if (await context.Db.Taxa.AsNoTracking().FirstOrDefaultAsync(taxon => taxon.Id == current, context.Cancellation) is not { } taxon)
            {
                break;
            }

            if (string.Equals(taxon.Rank, "genus", StringComparison.Ordinal))
            {
                genus = new { scientificName = taxon.ScientificName, commonName = taxon.CommonName };
            }
            else if (string.Equals(taxon.Rank, "family", StringComparison.Ordinal))
            {
                family = new { scientificName = taxon.ScientificName, commonName = taxon.CommonName };
                break;
            }

            at = taxon.ParentId;
        }

        return (family, genus);
    }

    /// <summary>
    /// Trays as the tools describe them, with totals, at most <see cref="MostRows"/> of them. Noted as sources only when
    /// asked for: a place or a plant asked about is the source, not every tray in it.
    /// </summary>
    private static object StockRows(List<ProductionEndpoints.ProductionRow> rows, ToolContext context, bool note = true)
    {
        var now = context.Clock.GetUtcNow();

        return new
        {
            trays = rows.Select(row => row.ContainerId).Distinct().Count(),
            plants = rows.Sum(row => row.Quantity),
            notCountedTrays = rows.Where(row => row.NotCounted).Select(row => row.ContainerId).Distinct().Count(),
            rows = rows.Take(MostRows).Select(row =>
            {
                if (note)
                {
                    context.Saw("tray", row.LabelCode, $"{row.LabelCode} {PlantName(row.CommonName, row.Variety)}");
                }

                return new
                {
                    tray = row.LabelCode,
                    plant = PlantName(row.CommonName, row.Variety),
                    batch = row.TraceabilityCode,
                    quantity = row.NotCounted ? (int?)null : row.Quantity,
                    stage = row.Stage?.ToString(),
                    daysAtStage = row.StageSince is { } since ? (int)(now - since).TotalDays : (int?)null,
                    place = row.LocationName,
                    pot = row.PotSizeName,
                };
            }),
            shown = Math.Min(rows.Count, MostRows),
        };
    }

    private static Guid? Id(JsonNode? arguments, string name) => Guid.TryParse(Text(arguments, name), out var id) ? id : null;
}
