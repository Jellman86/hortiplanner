using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Lookup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;
using static Hortiplanner.Tests.SpeciesLookupTests;

namespace Hortiplanner.Tests;

/// <summary>A plant's family tree: fetched once from GBIF, named from iNaturalist, kept, and shared between relatives.</summary>
public sealed class FamilyTreeTests(PostgresFixture database)
{
    private static string Match(string genus, long genusKey, string? species, long? speciesKey, string family = "Lamiaceae", long familyKey = 2497) => $$"""
        {
          "matchType": "EXACT", "status": "ACCEPTED", "rank": "{{(species is null ? "GENUS" : "SPECIES")}}",
          "canonicalName": "{{species ?? genus}}", "usageKey": {{speciesKey ?? genusKey}},
          "kingdom": "Plantae", "kingdomKey": 6, "phylum": "Tracheophyta", "phylumKey": 7707728,
          "class": "Magnoliopsida", "classKey": 220, "order": "Lamiales", "orderKey": 408,
          "family": "{{family}}", "familyKey": {{familyKey}}, "genus": "{{genus}}", "genusKey": {{genusKey}}
          {{(species is null ? string.Empty : $", \"species\": \"{species}\", \"speciesKey\": {speciesKey}")}}
        }
        """;

    private const string Ancestors = """
        {"results":[{"id":1,"name":"Lavandula angustifolia","rank":"species","preferred_common_name":"Common Lavender","ancestors":[
          {"rank":"kingdom","name":"Plantae","preferred_common_name":"Plants"},
          {"rank":"family","name":"Lamiaceae","preferred_common_name":"Mint Family"},
          {"rank":"genus","name":"Lavandula","preferred_common_name":"Lavenders"}]}]}
        """;

    private static async Task<Guid> PlantAsync(HttpClient owner, OutsideWorld world, long id, string name, string? common)
    {
        world.Autocomplete = $$"""{"results":[{{Taxon(id, name, common, "cc-by")}}]}""";
        var speciesId = (await owner.GetFromJsonAsync<JsonElement>(Route($"/api/species/search?q={Uri.EscapeDataString(name)}"), Cancellation))
            .GetProperty("species").EnumerateArray().Single().GetProperty("id").GetGuid();
        return Json(await PostForTextAsync(owner, "/api/plants/from-species", new { speciesId })).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> TreeAsync(HttpClient client, Guid plant) =>
        await client.GetFromJsonAsync<JsonElement>(Route($"/api/plants/{plant}/family-tree"), Cancellation);

    [Fact]
    public async Task A_plants_family_tree_runs_from_kingdom_to_species_and_lists_its_relatives_at_the_nursery()
    {
        var world = new OutsideWorld { Taxa = Ancestors };
        world.GbifMatches["Lavandula angustifolia"] = Match("Lavandula", 2927302, "Lavandula angustifolia", 2927305);
        world.GbifMatches["Lavandula stoechas"] = Match("Lavandula", 2927302, "Lavandula stoechas", 2927306);
        world.GbifMatches["Salvia rosmarinus"] = Match("Salvia", 2927303, "Salvia rosmarinus", 2927307);
        await using var app = await SpeciesLookupTests.CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        var lavender = await PlantAsync(owner, world, 1, "Lavandula angustifolia", "Common Lavender");
        var french = await PlantAsync(owner, world, 2, "Lavandula stoechas", "French Lavender");
        await PlantAsync(owner, world, 3, "Salvia rosmarinus", "Rosemary");

        var tree = await TreeAsync(owner, lavender);
        Assert.True(tree.GetProperty("linked").GetBoolean());
        Assert.Equal(["kingdom", "phylum", "class", "order", "family", "genus", "species"], tree.GetProperty("lineage").EnumerateArray().Select(rung => rung.GetProperty("rank").GetString()), StringComparer.Ordinal);
        var family = tree.GetProperty("lineage")[4];
        Assert.Equal("Lamiaceae", family.GetProperty("scientificName").GetString());
        Assert.Equal("Mint Family", family.GetProperty("commonName").GetString());

        // The others' trees are fetched as their pages are opened, then they are each other's relatives.
        await TreeAsync(owner, french);
        var rosemary = await TreeAsync(owner, (await owner.GetFromJsonAsync<JsonElement>(Route("/api/plants"), Cancellation)).EnumerateArray().Single(plant => string.Equals(plant.GetProperty("commonName").GetString(), "Rosemary", StringComparison.Ordinal)).GetProperty("id").GetGuid());
        Assert.Equal("Salvia", rosemary.GetProperty("lineage")[5].GetProperty("scientificName").GetString());

        tree = await TreeAsync(owner, lavender);
        Assert.Equal("French Lavender", Assert.Single(tree.GetProperty("sameGenus").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal("Rosemary", Assert.Single(tree.GetProperty("sameFamily").EnumerateArray()).GetProperty("name").GetString());

        // One row for the mint family, shared by all three.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();
        Assert.Equal(1, await db.Taxa.CountAsync(taxon => taxon.Rank == "family", Cancellation));
        Assert.Equal(2, await db.Taxa.CountAsync(taxon => taxon.Rank == "genus", Cancellation));
    }

    [Fact]
    public async Task A_family_tree_is_kept_so_it_shows_without_the_outside_service_and_waits_when_it_cant_be_reached()
    {
        var world = new OutsideWorld { Taxa = Ancestors };
        world.GbifMatches["Lavandula angustifolia"] = Match("Lavandula", 2927302, "Lavandula angustifolia", 2927305);
        await using var app = await SpeciesLookupTests.CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var lavender = await PlantAsync(owner, world, 1, "Lavandula angustifolia", "Common Lavender");

        world.Down = true;
        var waiting = await TreeAsync(owner, lavender);
        Assert.True(waiting.GetProperty("unavailable").GetBoolean());
        Assert.Empty(waiting.GetProperty("lineage").EnumerateArray());

        world.Down = false;
        Assert.Equal(7, (await TreeAsync(owner, lavender)).GetProperty("lineage").GetArrayLength());

        world.Down = true;
        var matches = world.Count("species/match");
        var kept = await TreeAsync(owner, lavender);
        Assert.False(kept.GetProperty("unavailable").GetBoolean());
        Assert.Equal(7, kept.GetProperty("lineage").GetArrayLength());
        Assert.Equal(matches, world.Count("species/match"));
    }

    [Fact]
    public async Task A_plant_matched_only_to_its_genus_stops_there_and_an_unmatched_plant_says_so()
    {
        var world = new OutsideWorld();
        world.GbifMatches["Lavandula"] = Match("Lavandula", 2927302, null, null);
        await using var app = await SpeciesLookupTests.CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var cross = await PlantAsync(owner, world, 4, "Lavandula", "Lavenders");

        var tree = await TreeAsync(owner, cross);
        Assert.Equal("genus", tree.GetProperty("lineage").EnumerateArray().Last().GetProperty("rank").GetString());

        var unmatched = Json(await PostForTextAsync(owner, "/api/plants", new { commonName = "Mystery seedling", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();
        Assert.False((await TreeAsync(owner, unmatched)).GetProperty("linked").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(owner, $"/api/plants/{Guid.CreateVersion7()}/family-tree"));
    }

    [Fact]
    public async Task Plants_matched_before_family_trees_were_kept_get_theirs_filled_in()
    {
        var world = new OutsideWorld { Taxa = Ancestors };
        world.GbifMatches["Lavandula angustifolia"] = Match("Lavandula", 2927302, "Lavandula angustifolia", 2927305);
        world.GbifMatches["Salvia rosmarinus"] = Match("Salvia", 2927303, "Salvia rosmarinus", 2927307);
        await using var app = await SpeciesLookupTests.CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        await PlantAsync(owner, world, 1, "Lavandula angustifolia", "Common Lavender");
        await PlantAsync(owner, world, 3, "Salvia rosmarinus", "Rosemary");

        var backfill = app.Services.GetRequiredService<LineageBackfill>();
        world.Down = true;
        Assert.Equal(0, await backfill.RunOnceAsync(Cancellation));
        world.Down = false;
        Assert.Equal(2, await backfill.RunOnceAsync(Cancellation));
        Assert.Equal(0, await backfill.RunOnceAsync(Cancellation));
    }

    [Fact]
    public async Task A_species_given_without_a_rank_doesnt_re_hang_the_family_its_relatives_share()
    {
        var world = new OutsideWorld { Taxa = Ancestors };
        world.GbifMatches["Lavandula angustifolia"] = Match("Lavandula", 2927302, "Lavandula angustifolia", 2927305);
        world.GbifMatches["Salvia rosmarinus"] = Match("Salvia", 2927303, "Salvia rosmarinus", 2927307).Replace("\"order\": \"Lamiales\", \"orderKey\": 408,", string.Empty, StringComparison.Ordinal);
        await using var app = await SpeciesLookupTests.CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var lavender = await PlantAsync(owner, world, 1, "Lavandula angustifolia", "Common Lavender");
        var rosemary = await PlantAsync(owner, world, 3, "Salvia rosmarinus", "Rosemary");

        await TreeAsync(owner, lavender);
        await TreeAsync(owner, rosemary);

        Assert.Contains("order", (await TreeAsync(owner, lavender)).GetProperty("lineage").EnumerateArray().Select(rung => rung.GetProperty("rank").GetString()), StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_plant_gbif_doesnt_know_isnt_asked_about_again_and_names_wait_for_inaturalist()
    {
        var world = new OutsideWorld { Taxa = Ancestors };
        await using var app = await SpeciesLookupTests.CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var unknown = await PlantAsync(owner, world, 5, "Plantus imaginarius", null);

        var tree = await TreeAsync(owner, unknown);
        Assert.True(tree.GetProperty("linked").GetBoolean());
        Assert.False(tree.GetProperty("unavailable").GetBoolean());
        Assert.Empty(tree.GetProperty("lineage").EnumerateArray());
        var asked = world.Count("species/match");
        await TreeAsync(owner, unknown);
        Assert.Equal(asked, world.Count("species/match"));

        // GBIF up but iNaturalist down: the tree waits, rather than being kept without its everyday names.
        world.GbifMatches["Lavandula angustifolia"] = Match("Lavandula", 2927302, "Lavandula angustifolia", 2927305);
        var lavender = await PlantAsync(owner, world, 1, "Lavandula angustifolia", "Common Lavender");
        world.INaturalistDown = true;
        Assert.True((await TreeAsync(owner, lavender)).GetProperty("unavailable").GetBoolean());
        world.INaturalistDown = false;
        Assert.Equal("Mint Family", (await TreeAsync(owner, lavender)).GetProperty("lineage")[4].GetProperty("commonName").GetString());
    }
}
