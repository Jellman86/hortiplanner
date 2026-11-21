using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hortiplanner.Api.Costs;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class CostTests(PostgresFixture database)
{
    private static async Task<Guid> AddPlantAsync(HttpClient owner, string name = "Lavender", string vatBand = "Standard") =>
        Json(await PostForTextAsync(owner, "/api/plants", new { commonName = name, protectedVariety = false, vatBand })).GetProperty("id").GetGuid();

    private static async Task<(Guid Batch, Guid Tray)> StartAsync(HttpClient client, Guid plant, string source = "SeedLot", int quantity = 40, Guid? purchaseLineId = null, Guid? supplierId = null)
    {
        var started = Json(await PostForTextAsync(client, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddDays(-5), plantId = plant, quantity, source, supplierId, originCountry = "GB", containerKind = "Tray", purchaseLineId }));
        return (started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid());
    }

    private static object Cost(string answer = "Amount", decimal? amount = 2.99m, string kind = "Material", Guid? id = null, DateTimeOffset? occurredAt = null, string? vatBand = null, bool? includesVat = null, string? note = null, Guid? replacesId = null) =>
        new { eventId = id ?? Guid.NewGuid(), occurredAt = occurredAt ?? DateTimeOffset.UtcNow, kind, answer, amount, vatBand, includesVat, note, replacesId };

    private static async Task<HttpStatusCode> AddCostAsync(HttpClient client, Guid batch, object body) =>
        await PostStatusAsync(client, $"/api/batches/{batch}/costs", body);

    private static async Task<(HttpStatusCode Status, string? Problem)> AddCostForProblemAsync(HttpClient client, Guid batch, object body)
    {
        using var response = await client.PostAsJsonAsync(Route($"/api/batches/{batch}/costs"), body, Cancellation);
        var text = await response.Content.ReadAsStringAsync(Cancellation);
        return (response.StatusCode, response.StatusCode == HttpStatusCode.Conflict ? Json(text).GetProperty("problem").GetString() : null);
    }

    /// <summary>The seed or plants' answer that stands now, for the next answer to replace.</summary>
    private static async Task<Guid> StandingAsync(HttpClient owner, Guid batch) =>
        (await CostingAsync(owner, batch)).GetProperty("history").EnumerateArray()
            .Last(entry => string.Equals(entry.GetProperty("kind").GetString(), "Material", StringComparison.Ordinal) && !entry.GetProperty("replaced").GetBoolean())
            .GetProperty("id").GetGuid();

    private static Task<JsonElement> CostingAsync(HttpClient owner, Guid batch) =>
        owner.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batch}/costing"), Cancellation);

    private static (decimal WithVat, decimal BeforeVat) Pair(JsonElement money) =>
        (money.GetProperty("withVat").GetDecimal(), money.GetProperty("beforeVat").GetDecimal());

    private static async Task<bool> CostMissingAsync(HttpClient client, Guid tray) =>
        (await client.GetFromJsonAsync<JsonElement>(Route("/api/containers/active"), Cancellation)).EnumerateArray()
            .Single(container => container.GetProperty("id").GetGuid() == tray).GetProperty("contents")[0].GetProperty("costMissing").GetBoolean();

    [Fact]
    public async Task A_typed_cost_is_taken_as_including_VAT_at_the_plants_rate_and_kept_as_two_figures()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, _) = await StartAsync(nursery.Staff, await AddPlantAsync(nursery.Owner));

        Assert.Equal(HttpStatusCode.Created, await AddCostAsync(nursery.Staff, batch, Cost(amount: 2.99m)));

        var costing = await CostingAsync(nursery.Owner, batch);
        Assert.Equal("Amount", costing.GetProperty("material").GetProperty("state").GetString());
        Assert.Equal((2.99m, 2.49m), Pair(costing.GetProperty("material").GetProperty("cost")));
        Assert.Equal((2.99m, 2.49m), Pair(costing.GetProperty("total")));
        Assert.Equal("WithVat", costing.GetProperty("counts").GetString());

        var entry = costing.GetProperty("material").GetProperty("entry");
        Assert.Equal(20m, entry.GetProperty("vatRate").GetDecimal());
        Assert.Equal("Standard", entry.GetProperty("vatBand").GetString());
        Assert.True(entry.GetProperty("includesVat").GetBoolean());
        Assert.False(entry.GetProperty("vatSaid").GetBoolean());
        Assert.Equal("Alex Moss", entry.GetProperty("addedBy").GetString());
    }

    [Fact]
    public async Task A_zero_rated_plants_seed_has_the_same_figure_twice_and_anything_else_is_standard_rate()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, _) = await StartAsync(nursery.Owner, await AddPlantAsync(nursery.Owner, "Basil", "Zero"));

        await AddCostAsync(nursery.Owner, batch, Cost(amount: 1.80m));
        Assert.Equal(HttpStatusCode.Created, await AddCostAsync(nursery.Owner, batch, Cost(kind: "Other", amount: 3m, note: "Slug pellets")));

        var costing = await CostingAsync(nursery.Owner, batch);
        Assert.Equal((1.80m, 1.80m), Pair(costing.GetProperty("material").GetProperty("cost")));
        var other = Assert.Single(costing.GetProperty("others").EnumerateArray());
        Assert.Equal((3m, 2.50m), Pair(other.GetProperty("cost")));
        Assert.Equal("Slug pellets", other.GetProperty("note").GetString());
        Assert.Equal((4.80m, 4.30m), Pair(costing.GetProperty("total")));
    }

    [Theory]
    [InlineData("10.00", "20", true, "8.33", "1.67")]
    [InlineData("0.05", "20", true, "0.04", "0.01")]
    [InlineData("2.50", "20", false, "2.50", "0.50")]
    [InlineData("1.05", "5", true, "1.00", "0.05")]
    [InlineData("0.10", "5", false, "0.10", "0.01")]
    public void A_cost_is_split_to_the_penny_as_invoices_round(string amount, string rate, bool includesVat, string net, string vat) =>
        Assert.Equal(
            (decimal.Parse(net, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(vat, System.Globalization.CultureInfo.InvariantCulture)),
            CostEndpoints.Split(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), includesVat));

    [Fact]
    public async Task A_cost_keeps_the_rate_of_its_day_whatever_the_rate_is_changed_to()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, _) = await StartAsync(nursery.Owner, await AddPlantAsync(nursery.Owner));
        await AddCostAsync(nursery.Owner, batch, Cost(amount: 12m));

        using (var response = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/vat-rates"), new { standard = 25m, reduced = 5m }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal((12m, 10m), Pair((await CostingAsync(nursery.Owner, batch)).GetProperty("material").GetProperty("cost")));

        // Something written up for an earlier day takes the rate of that day, and today's takes today's.
        await AddCostAsync(nursery.Owner, batch, Cost(kind: "Other", amount: 12m, note: "Feed", occurredAt: DateTimeOffset.UtcNow.AddDays(-3)));
        await AddCostAsync(nursery.Owner, batch, Cost(kind: "Other", amount: 12.50m, note: "Canes"));
        var others = (await CostingAsync(nursery.Owner, batch)).GetProperty("others").EnumerateArray().ToList();
        Assert.Equal((12m, 10m), Pair(others[0].GetProperty("cost")));
        Assert.Equal((12.50m, 10m), Pair(others[1].GetProperty("cost")));
        Assert.Equal(25m, others[1].GetProperty("vatRate").GetDecimal());
    }

    [Fact]
    public async Task Each_answer_and_a_replacement_keeps_the_old_entry()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, tray) = await StartAsync(nursery.Staff, await AddPlantAsync(nursery.Owner));
        Assert.True(await CostMissingAsync(nursery.Staff, tray));
        Assert.Equal("NoAnswer", (await CostingAsync(nursery.Owner, batch)).GetProperty("material").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, (await CostingAsync(nursery.Owner, batch)).GetProperty("total").ValueKind);

        await AddCostAsync(nursery.Staff, batch, Cost(amount: 2.50m));
        Assert.False(await CostMissingAsync(nursery.Staff, tray));

        // A second answer given without seeing the first is refused, so nobody's answer is overwritten.
        Assert.Equal((HttpStatusCode.Conflict, "Answered"), await AddCostForProblemAsync(nursery.Staff, batch, Cost(amount: 3m)));
        Assert.Equal((HttpStatusCode.Conflict, "Answered"), await AddCostForProblemAsync(nursery.Owner, batch, Cost(answer: "NotKnown", amount: null)));

        var first = await StandingAsync(nursery.Owner, batch);
        Assert.Equal(HttpStatusCode.Created, await AddCostAsync(nursery.Owner, batch, Cost(answer: "Free", amount: null, replacesId: first)));
        Assert.Equal((HttpStatusCode.Conflict, "Replaced"), await AddCostForProblemAsync(nursery.Owner, batch, Cost(amount: 4m, replacesId: first)));
        var costing = await CostingAsync(nursery.Owner, batch);
        Assert.Equal("Free", costing.GetProperty("material").GetProperty("state").GetString());
        Assert.Equal((0m, 0m), Pair(costing.GetProperty("total")));

        await AddCostAsync(nursery.Owner, batch, Cost(answer: "NotKnown", amount: null, replacesId: await StandingAsync(nursery.Owner, batch)));
        costing = await CostingAsync(nursery.Owner, batch);
        Assert.Equal("NotKnown", costing.GetProperty("material").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, costing.GetProperty("total").ValueKind);
        Assert.False(await CostMissingAsync(nursery.Staff, tray));

        var history = costing.GetProperty("history").EnumerateArray().ToList();
        Assert.Equal(["Amount", "Free", "NotKnown"], history.Select(entry => entry.GetProperty("answer").GetString()!), StringComparer.Ordinal);
        Assert.Equal([true, true, false], history.Select(entry => entry.GetProperty("replaced").GetBoolean()));
        Assert.Equal(history[0].GetProperty("id").GetGuid(), history[1].GetProperty("replacesId").GetGuid());

        // Removed, it goes back to having no answer, and the tray asks again.
        var notKnown = await StandingAsync(nursery.Owner, batch);
        Assert.Equal(HttpStatusCode.Created, await AddCostAsync(nursery.Owner, batch, Cost(answer: "Removed", amount: null, replacesId: notKnown)));
        Assert.Equal("NoAnswer", (await CostingAsync(nursery.Owner, batch)).GetProperty("material").GetProperty("state").GetString());
        Assert.True(await CostMissingAsync(nursery.Staff, tray));
        Assert.Equal(HttpStatusCode.Conflict, await AddCostAsync(nursery.Owner, batch, Cost(answer: "Removed", amount: null, replacesId: notKnown)));

        // After a removal anyone can answer again, and the chain of answers carries on.
        Assert.Equal(HttpStatusCode.Created, await AddCostAsync(nursery.Staff, batch, Cost(amount: 1.99m)));
        var chain = (await CostingAsync(nursery.Owner, batch)).GetProperty("history").EnumerateArray().ToList();
        Assert.Equal(chain[^2].GetProperty("id").GetGuid(), chain[^1].GetProperty("replacesId").GetGuid());
        Assert.Equal("Amount", (await CostingAsync(nursery.Owner, batch)).GetProperty("material").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Something_else_added_stands_until_changed_or_removed_and_only_once()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, _) = await StartAsync(nursery.Owner, await AddPlantAsync(nursery.Owner));
        var feed = Guid.NewGuid();
        await AddCostAsync(nursery.Owner, batch, Cost(kind: "Other", amount: 6m, note: "Feed", id: feed));
        await AddCostAsync(nursery.Owner, batch, Cost(kind: "Other", amount: 3m, note: "Canes"));

        var changed = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, await AddCostAsync(nursery.Owner, batch, Cost(kind: "Other", amount: 7.20m, note: "Feed", id: changed, replacesId: feed)));
        Assert.Equal(HttpStatusCode.Conflict, await AddCostAsync(nursery.Owner, batch, Cost(kind: "Other", amount: 8m, note: "Feed", replacesId: feed)));
        Assert.Equal(HttpStatusCode.Created, await AddCostAsync(nursery.Owner, batch, Cost(kind: "Other", answer: "Removed", amount: null, replacesId: changed)));

        var others = (await CostingAsync(nursery.Owner, batch)).GetProperty("others").EnumerateArray().ToList();
        Assert.Equal("Canes", Assert.Single(others).GetProperty("note").GetString());
    }

    [Fact]
    public async Task A_resend_is_stored_once_and_an_id_used_for_something_else_is_refused()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, _) = await StartAsync(nursery.Staff, await AddPlantAsync(nursery.Owner));
        var id = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;

        Assert.Equal(HttpStatusCode.Created, await AddCostAsync(nursery.Staff, batch, Cost(id: id, occurredAt: at)));
        Assert.Equal(HttpStatusCode.OK, await AddCostAsync(nursery.Staff, batch, Cost(id: id, occurredAt: at)));
        Assert.Equal(HttpStatusCode.Conflict, await AddCostAsync(nursery.Staff, batch, Cost(id: id, occurredAt: at, amount: 3.99m)));
        Assert.Equal(HttpStatusCode.Conflict, await AddCostAsync(nursery.Owner, batch, Cost(id: id, occurredAt: at)));

        Assert.Single((await CostingAsync(nursery.Owner, batch)).GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task Staff_can_say_what_it_cost_but_not_see_costs_or_say_what_only_the_owner_can()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, _) = await StartAsync(nursery.Staff, await AddPlantAsync(nursery.Owner));

        Assert.Equal(HttpStatusCode.Created, await AddCostAsync(nursery.Staff, batch, Cost(answer: "Free", amount: null)));
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, $"/api/batches/{batch}/costing"));
        Assert.Equal(HttpStatusCode.Forbidden, await AddCostAsync(nursery.Staff, batch, Cost(answer: "NotKnown", amount: null)));
        Assert.Equal(HttpStatusCode.Forbidden, await AddCostAsync(nursery.Staff, batch, Cost(answer: "Removed", amount: null, replacesId: Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.Forbidden, await AddCostAsync(nursery.Staff, batch, Cost(vatBand: "Zero", includesVat: true)));
        Assert.Equal(HttpStatusCode.Forbidden, await AddCostAsync(nursery.Staff, batch, Cost(kind: "Other", amount: 1m, note: "Feed")));
        Assert.Equal(HttpStatusCode.Forbidden, await AddCostAsync(nursery.Staff, batch, Cost(amount: 1m, replacesId: Guid.NewGuid())));
    }

    [Fact]
    public async Task Costs_that_make_no_sense_are_refused()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, _) = await StartAsync(nursery.Owner, await AddPlantAsync(nursery.Owner));

        Assert.Equal(HttpStatusCode.BadRequest, await AddCostAsync(nursery.Owner, batch, Cost(amount: 0m)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddCostAsync(nursery.Owner, batch, Cost(amount: 100_000m)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddCostAsync(nursery.Owner, batch, Cost(amount: 2.999m)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddCostAsync(nursery.Owner, batch, Cost(amount: null)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddCostAsync(nursery.Owner, batch, Cost(answer: "Free", amount: 1m)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddCostAsync(nursery.Owner, batch, Cost(vatBand: "Zero")));
        Assert.Equal(HttpStatusCode.BadRequest, await AddCostAsync(nursery.Owner, batch, Cost(kind: "Other", amount: 1m)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddCostAsync(nursery.Owner, batch, Cost(kind: "Other", answer: "Free", amount: null, note: "Feed")));
        Assert.Equal(HttpStatusCode.BadRequest, await AddCostAsync(nursery.Owner, batch, Cost(kind: "Other", answer: "Removed", amount: null)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddCostAsync(nursery.Owner, batch, Cost(answer: "Removed", amount: null)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddCostAsync(nursery.Owner, batch, Cost(kind: "Other", amount: 1m, note: new string('x', 201))));
        Assert.Equal(HttpStatusCode.NotFound, await AddCostAsync(nursery.Owner, Guid.NewGuid(), Cost()));
    }

    [Fact]
    public async Task The_owner_can_say_what_VAT_was_in_a_cost_and_it_replaces_the_assumed_one()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, _) = await StartAsync(nursery.Staff, await AddPlantAsync(nursery.Owner));
        var at = DateTimeOffset.UtcNow.AddDays(-1);
        await AddCostAsync(nursery.Staff, batch, Cost(amount: 2.49m, occurredAt: at));

        // Sent with today's time, it still keeps the day the cost was for.
        Assert.Equal(HttpStatusCode.Created, await AddCostAsync(nursery.Owner, batch, Cost(amount: 2.49m, vatBand: "Standard", includesVat: false, replacesId: await StandingAsync(nursery.Owner, batch))));

        var costing = await CostingAsync(nursery.Owner, batch);
        Assert.Equal((2.99m, 2.49m), Pair(costing.GetProperty("material").GetProperty("cost")));
        Assert.True(costing.GetProperty("material").GetProperty("entry").GetProperty("vatSaid").GetBoolean());
        Assert.Equal(at.UtcDateTime, costing.GetProperty("material").GetProperty("entry").GetProperty("occurredAt").GetDateTimeOffset().UtcDateTime, TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, costing.GetProperty("history").GetArrayLength());

        await AddCostAsync(nursery.Owner, batch, Cost(amount: 2.49m, vatBand: "Zero", includesVat: true, replacesId: await StandingAsync(nursery.Owner, batch)));
        Assert.Equal((2.49m, 2.49m), Pair((await CostingAsync(nursery.Owner, batch)).GetProperty("material").GetProperty("cost")));
    }

    [Fact]
    public async Task A_batch_from_a_purchase_takes_its_share_of_the_line_until_the_purchase_is_voided()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var supplier = Json(await PostForTextAsync(nursery.Owner, "/api/suppliers", new { name = "Example Plugs" })).GetProperty("id").GetGuid();
        var purchase = Json(await PostForTextAsync(nursery.Owner, "/api/purchases", new { supplierId = supplier, purchasedOn = DateOnly.FromDateTime(DateTime.UtcNow), lines = new[] { new { description = "Lavender plugs", quantity = 40, netCost = 30m, vat = 6m, isPlants = true } } })).GetProperty("id").GetGuid();
        var line = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/purchases"), Cancellation))[0].GetProperty("lines")[0].GetProperty("id").GetGuid();
        var plant = await AddPlantAsync(nursery.Owner);
        var (first, tray) = await StartAsync(nursery.Owner, plant, "BoughtIn", 30, line, supplier);
        await StartAsync(nursery.Owner, plant, "BoughtIn", 10, line, supplier);
        Assert.False(await CostMissingAsync(nursery.Owner, tray));

        // A cost typed in as well is kept, but the purchase gives the cost.
        await AddCostAsync(nursery.Staff, first, Cost(amount: 20m));
        var costing = await CostingAsync(nursery.Owner, first);
        var material = costing.GetProperty("material");
        Assert.Equal("Purchase", material.GetProperty("state").GetString());
        Assert.Equal((27m, 22.50m), Pair(material.GetProperty("cost")));
        Assert.Equal("Example Plugs", material.GetProperty("purchase").GetProperty("supplier").GetString());
        Assert.True(material.GetProperty("purchase").GetProperty("shared").GetBoolean());
        Assert.Equal(20m, material.GetProperty("superseded").GetProperty("amountPaid").GetDecimal());

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/purchases/{purchase}/void"));
        Assert.Equal("Amount", (await CostingAsync(nursery.Owner, first)).GetProperty("material").GetProperty("state").GetString());
    }

    private static async Task<(Guid Supplier, Guid Purchase, Guid Line)> BuyAsync(HttpClient owner, decimal net, decimal vat)
    {
        var supplier = Json(await PostForTextAsync(owner, "/api/suppliers", new { name = "Example Plugs" })).GetProperty("id").GetGuid();
        var purchase = Json(await PostForTextAsync(owner, "/api/purchases", new { supplierId = supplier, purchasedOn = DateOnly.FromDateTime(DateTime.UtcNow), lines = new[] { new { description = "Plugs", quantity = 40, netCost = net, vat, isPlants = true } } })).GetProperty("id").GetGuid();
        var line = (await owner.GetFromJsonAsync<JsonElement>(Route("/api/purchases"), Cancellation))[0].GetProperty("lines")[0].GetProperty("id").GetGuid();
        return (supplier, purchase, line);
    }

    private static async Task<(Guid Batch, Guid Start)> StartFromAsync(HttpClient client, Guid plant, Guid line, Guid supplier, int quantity, string source = "BoughtIn", string? sownAs = null)
    {
        var start = Guid.NewGuid();
        var started = Json(await PostForTextAsync(client, "/api/batches", new { eventId = start, occurredAt = DateTimeOffset.UtcNow.AddDays(-5), plantId = plant, quantity, source, supplierId = supplier, originCountry = "GB", containerKind = "Tray", purchaseLineId = line, sownAs }));
        return (started.GetProperty("batchId").GetGuid(), start);
    }

    [Fact]
    public async Task A_start_that_was_undone_takes_no_share_and_a_corrected_count_shares_by_the_new_count()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (supplier, _, line) = await BuyAsync(nursery.Owner, 30m, 6m);
        var plant = await AddPlantAsync(nursery.Owner);
        var (undone, undoneStart) = await StartFromAsync(nursery.Owner, plant, line, supplier, 40);
        var (first, firstStart) = await StartFromAsync(nursery.Owner, plant, line, supplier, 30);
        var (second, _) = await StartFromAsync(nursery.Owner, plant, line, supplier, 10);

        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Owner, $"/api/events/{undoneStart}/correct", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow }));
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Owner, $"/api/events/{firstStart}/correct", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, quantity = 20 }));

        Assert.Equal("NoAnswer", (await CostingAsync(nursery.Owner, undone)).GetProperty("material").GetProperty("state").GetString());
        Assert.Equal((24m, 20m), Pair((await CostingAsync(nursery.Owner, first)).GetProperty("material").GetProperty("cost")));
        Assert.Equal((12m, 10m), Pair((await CostingAsync(nursery.Owner, second)).GetProperty("material").GetProperty("cost")));
    }

    [Fact]
    public async Task Shares_are_equal_when_a_batch_was_sown_uncounted_and_always_add_up_to_the_line()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (supplier, _, line) = await BuyAsync(nursery.Owner, 10m, 0m);
        var plant = await AddPlantAsync(nursery.Owner);
        var batches = new List<Guid>
        {
            (await StartFromAsync(nursery.Owner, plant, line, supplier, 0, "SeedLot", "Unknown")).Batch,
            (await StartFromAsync(nursery.Owner, plant, line, supplier, 50, "SeedLot")).Batch,
            (await StartFromAsync(nursery.Owner, plant, line, supplier, 25, "SeedLot")).Batch,
        };

        var materials = new List<JsonElement>();
        foreach (var batch in batches)
        {
            materials.Add((await CostingAsync(nursery.Owner, batch)).GetProperty("material"));
        }

        Assert.All(materials, material => Assert.True(material.GetProperty("purchase").GetProperty("equally").GetBoolean()));
        Assert.Equal(10m, materials.Sum(material => material.GetProperty("cost").GetProperty("beforeVat").GetDecimal()));
        Assert.All(materials, material => Assert.InRange(material.GetProperty("cost").GetProperty("beforeVat").GetDecimal(), 3.33m, 3.34m));
    }

    [Fact]
    public async Task A_voided_purchase_leaves_the_tray_asking_again()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (supplier, purchase, line) = await BuyAsync(nursery.Owner, 30m, 6m);
        var plant = await AddPlantAsync(nursery.Owner);
        await StartFromAsync(nursery.Owner, plant, line, supplier, 40);
        var tray = (await nursery.Staff.GetFromJsonAsync<JsonElement>(Route("/api/containers/active"), Cancellation))[0].GetProperty("id").GetGuid();
        Assert.False(await CostMissingAsync(nursery.Staff, tray));

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/purchases/{purchase}/void"));

        Assert.True(await CostMissingAsync(nursery.Staff, tray));
    }

    [Fact]
    public async Task Two_answers_at_once_leave_one_standing()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, _) = await StartAsync(nursery.Staff, await AddPlantAsync(nursery.Owner));

        var results = await Task.WhenAll(
            AddCostForProblemAsync(nursery.Staff, batch, Cost(amount: 2.99m)),
            AddCostForProblemAsync(nursery.Owner, batch, Cost(amount: 3.49m)));

        Assert.Single(results, result => result.Status == HttpStatusCode.Created);
        Assert.Single(results, result => result == (HttpStatusCode.Conflict, "Answered"));
        Assert.Single((await CostingAsync(nursery.Owner, batch)).GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task A_cost_dated_ahead_is_taken_as_now_and_a_resend_from_a_wrong_clock_is_still_the_same_cost()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, _) = await StartAsync(nursery.Staff, await AddPlantAsync(nursery.Owner));
        var id = Guid.NewGuid();
        var body = Cost(id: id, occurredAt: DateTimeOffset.UtcNow.AddDays(2));

        async Task<HttpStatusCode> SendAsync(TimeSpan clockOut)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Route($"/api/batches/{batch}/costs")) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-Device-Time", (DateTimeOffset.UtcNow + clockOut).ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            using var response = await nursery.Staff.SendAsync(request, Cancellation);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.Created, await SendAsync(TimeSpan.Zero));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(TimeSpan.FromHours(3)));

        var stored = (await CostingAsync(nursery.Owner, batch)).GetProperty("material").GetProperty("entry").GetProperty("occurredAt").GetDateTimeOffset();
        Assert.InRange(stored, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Cuttings_taken_here_cost_nothing_to_buy_and_the_tray_never_asks()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, tray) = await StartAsync(nursery.Owner, await AddPlantAsync(nursery.Owner), "Cuttings");

        Assert.False(await CostMissingAsync(nursery.Staff, tray));
        var costing = await CostingAsync(nursery.Owner, batch);
        Assert.Equal("Cuttings", costing.GetProperty("material").GetProperty("state").GetString());
        Assert.Equal((0m, 0m), Pair(costing.GetProperty("total")));
    }

    [Fact]
    public async Task With_costs_turned_off_the_costing_is_gone_and_a_cost_waiting_on_a_phone_is_still_taken()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, tray) = await StartAsync(nursery.Staff, await AddPlantAsync(nursery.Owner));
        using (var response = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/features/Costs"), new { on = false }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Owner, $"/api/batches/{batch}/costing"));
        Assert.False(await CostMissingAsync(nursery.Staff, tray));
        Assert.Equal(HttpStatusCode.Created, await AddCostAsync(nursery.Staff, batch, Cost()));
    }

    [Fact]
    public async Task A_registered_nursery_counts_the_figures_before_VAT()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        using (var response = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { name = "Croft View", vatNumber = "GB123456789", hasLogo = false, vatRegistered = true, paymentTermsDays = 14 }, Cancellation))
        {
            Assert.True(response.IsSuccessStatusCode);
        }

        var (batch, _) = await StartAsync(nursery.Owner, await AddPlantAsync(nursery.Owner));

        Assert.Equal("BeforeVat", (await CostingAsync(nursery.Owner, batch)).GetProperty("counts").GetString());
    }

    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow.AddHours(-1);

    private static object NewBatch(Guid plant, Guid? id = null, string source = "SeedLot", decimal? costAmount = null, bool costNothing = false, Guid? purchaseLineId = null, Guid? supplierId = null) =>
        new { eventId = id ?? Guid.NewGuid(), occurredAt = StartedAt, plantId = plant, quantity = 40, source, supplierId, originCountry = "GB", containerKind = "Tray", purchaseLineId, costAmount, costNothing };

    [Fact]
    public async Task New_batch_saves_what_it_cost_with_the_batch_and_a_retry_saves_it_once()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        var id = Guid.NewGuid();

        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", NewBatch(plant, id, costAmount: 2.99m)));
        Assert.Equal(HttpStatusCode.OK, await PostStatusAsync(nursery.Staff, "/api/batches", NewBatch(plant, id, costAmount: 2.99m)));

        var costing = await CostingAsync(nursery.Owner, started.GetProperty("batchId").GetGuid());
        Assert.Equal((2.99m, 2.49m), Pair(costing.GetProperty("material").GetProperty("cost")));
        Assert.Equal("Alex Moss", costing.GetProperty("material").GetProperty("entry").GetProperty("addedBy").GetString());
        Assert.Single(costing.GetProperty("history").EnumerateArray());
        Assert.False(await CostMissingAsync(nursery.Staff, started.GetProperty("containerId").GetGuid()));

        // The same id with a different cost is a different request, and the first cost stands.
        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(nursery.Staff, "/api/batches", NewBatch(plant, id, costAmount: 3.50m)));
        Assert.Equal(2.99m, (await CostingAsync(nursery.Owner, started.GetProperty("batchId").GetGuid())).GetProperty("material").GetProperty("entry").GetProperty("amountPaid").GetDecimal());

        var free = Json(await PostForTextAsync(nursery.Staff, "/api/batches", NewBatch(plant, costNothing: true))).GetProperty("batchId").GetGuid();
        Assert.Equal("Free", (await CostingAsync(nursery.Owner, free)).GetProperty("material").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Bought_in_plants_not_from_a_purchase_keep_the_cost_typed_at_New_batch()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var supplier = Json(await PostForTextAsync(nursery.Owner, "/api/suppliers", new { name = "Example Plugs" })).GetProperty("id").GetGuid();
        var plant = await AddPlantAsync(nursery.Owner);

        var batch = Json(await PostForTextAsync(nursery.Staff, "/api/batches", NewBatch(plant, source: "BoughtIn", costAmount: 24m, supplierId: supplier))).GetProperty("batchId").GetGuid();

        Assert.Equal((24m, 20m), Pair((await CostingAsync(nursery.Owner, batch)).GetProperty("material").GetProperty("cost")));
    }

    [Fact]
    public async Task A_cost_for_a_plant_that_doesnt_exist_is_refused_not_a_crash()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Staff, "/api/batches", NewBatch(Guid.NewGuid(), costAmount: 2.99m)));
    }

    [Fact]
    public async Task A_cost_that_wont_do_refuses_the_whole_batch()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Staff, "/api/batches", NewBatch(plant, costAmount: 0m)));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Staff, "/api/batches", NewBatch(plant, costAmount: 2.999m)));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Staff, "/api/batches", NewBatch(plant, costNothing: true, costAmount: 1m)));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Staff, "/api/batches", NewBatch(plant, costAmount: -1m)));

        Assert.Equal(0, (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/containers/active"), Cancellation)).GetArrayLength());
    }

    [Fact]
    public async Task New_batch_keeps_no_typed_cost_for_cuttings_or_plants_from_a_purchase()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (supplier, _, line) = await BuyAsync(nursery.Owner, 30m, 6m);
        var plant = await AddPlantAsync(nursery.Owner);

        var bought = Json(await PostForTextAsync(nursery.Owner, "/api/batches", NewBatch(plant, source: "BoughtIn", costAmount: 50m, purchaseLineId: line, supplierId: supplier))).GetProperty("batchId").GetGuid();
        var cuttings = Json(await PostForTextAsync(nursery.Owner, "/api/batches", NewBatch(plant, source: "Cuttings", costAmount: 5m))).GetProperty("batchId").GetGuid();

        var fromPurchase = await CostingAsync(nursery.Owner, bought);
        Assert.Equal("Purchase", fromPurchase.GetProperty("material").GetProperty("state").GetString());
        Assert.Empty(fromPurchase.GetProperty("history").EnumerateArray());
        Assert.Empty((await CostingAsync(nursery.Owner, cuttings)).GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task Costs_to_fill_in_lists_only_batches_with_no_answer_oldest_first_with_their_trays()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner, "Lavender");
        var (older, olderTray) = await StartAsync(nursery.Staff, plant);
        var newer = Json(await PostForTextAsync(nursery.Staff, "/api/batches", NewBatch(plant))).GetProperty("batchId").GetGuid();
        var answered = Json(await PostForTextAsync(nursery.Staff, "/api/batches", NewBatch(plant, costAmount: 2m))).GetProperty("batchId").GetGuid();
        await PostForTextAsync(nursery.Staff, "/api/batches", NewBatch(plant, source: "Cuttings"));

        var missing = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/costs/missing"), Cancellation)).EnumerateArray().ToList();

        Assert.Equal([older, newer], missing.Select(row => row.GetProperty("batchId").GetGuid()));
        Assert.DoesNotContain(missing, row => row.GetProperty("batchId").GetGuid() == answered);
        Assert.Equal("Lavender", missing[0].GetProperty("commonName").GetString());
        var label = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/containers/active"), Cancellation)).EnumerateArray()
            .Single(container => container.GetProperty("id").GetGuid() == olderTray).GetProperty("labelCode").GetString();
        Assert.Equal([label!], missing[0].GetProperty("trays").EnumerateArray().Select(tray => tray.GetString()!), StringComparer.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/costs/missing"));
    }

    [Fact]
    public async Task Saying_none_of_them_is_known_marks_each_in_one_go_and_leaves_an_answer_given_meanwhile()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        var (first, _) = await StartAsync(nursery.Staff, plant);
        var (second, _) = await StartAsync(nursery.Staff, plant);
        var (third, _) = await StartAsync(nursery.Staff, plant);

        // The third is answered after the list was fetched, and before the owner says none of them is known.
        await AddCostAsync(nursery.Staff, third, Cost(amount: 1.50m));
        // The first had a cost removed, so its answer follows on from the removal.
        await AddCostAsync(nursery.Staff, first, Cost(amount: 9m));
        await AddCostAsync(nursery.Owner, first, Cost(answer: "Removed", amount: null, replacesId: await StandingAsync(nursery.Owner, first)));

        var marked = Json(await PostForTextAsync(nursery.Owner, "/api/costs/not-known", new { batchIds = new[] { first, second, third, first } }));

        Assert.Equal(2, marked.GetProperty("marked").GetInt32());
        Assert.Equal("NotKnown", (await CostingAsync(nursery.Owner, first)).GetProperty("material").GetProperty("state").GetString());
        Assert.Equal("NotKnown", (await CostingAsync(nursery.Owner, second)).GetProperty("material").GetProperty("state").GetString());
        Assert.Equal("Amount", (await CostingAsync(nursery.Owner, third)).GetProperty("material").GetProperty("state").GetString());
        Assert.Empty((await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/costs/missing"), Cancellation)).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Staff, "/api/costs/not-known", new { batchIds = new[] { first } }));
    }

    private static async Task<List<Guid>> MissingIdsAsync(HttpClient owner) =>
        [.. (await owner.GetFromJsonAsync<JsonElement>(Route("/api/costs/missing"), Cancellation)).EnumerateArray().Select(row => row.GetProperty("batchId").GetGuid())];

    [Fact]
    public async Task A_batch_undone_at_its_start_has_nothing_to_fill_in_and_one_from_a_voided_purchase_does()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        var start = Guid.NewGuid();
        var undone = Json(await PostForTextAsync(nursery.Staff, "/api/batches", NewBatch(plant, start))).GetProperty("batchId").GetGuid();
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/events/{start}/correct", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow }));
        var (supplier, purchase, line) = await BuyAsync(nursery.Owner, 30m, 6m);
        var (bought, _) = await StartFromAsync(nursery.Owner, plant, line, supplier, 40);

        Assert.DoesNotContain(undone, await MissingIdsAsync(nursery.Owner));
        Assert.DoesNotContain(bought, await MissingIdsAsync(nursery.Owner));

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/purchases/{purchase}/void"));
        Assert.Equal([bought], await MissingIdsAsync(nursery.Owner));
    }

    [Fact]
    public async Task Saying_none_is_known_ignores_batches_that_need_nothing_and_refuses_no_list()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        var cuttings = Json(await PostForTextAsync(nursery.Staff, "/api/batches", NewBatch(plant, source: "Cuttings"))).GetProperty("batchId").GetGuid();

        Assert.Equal(0, Json(await PostForTextAsync(nursery.Owner, "/api/costs/not-known", new { batchIds = Array.Empty<Guid>() })).GetProperty("marked").GetInt32());
        Assert.Equal(0, Json(await PostForTextAsync(nursery.Owner, "/api/costs/not-known", new { batchIds = new[] { cuttings, Guid.NewGuid() } })).GetProperty("marked").GetInt32());
        Assert.Empty((await CostingAsync(nursery.Owner, cuttings)).GetProperty("history").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Owner, "/api/costs/not-known", new { }));
    }

    [Fact]
    public async Task With_costs_turned_off_there_is_nothing_to_fill_in()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        using (var off = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/features/Costs"), new { on = false }, Cancellation))
        {
            off.EnsureSuccessStatusCode();
        }

        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Owner, "/api/costs/missing"));
        Assert.Equal(HttpStatusCode.NotFound, await PostStatusAsync(nursery.Owner, "/api/costs/not-known", new { batchIds = Array.Empty<Guid>() }));
    }

    private static async Task<Guid> AddPotSizeAsync(HttpClient owner, string name) =>
        Json(await PostForTextAsync(owner, "/api/pot-sizes", new { name })).GetProperty("id").GetGuid();

    private static async Task<JsonElement> SetAllowanceAsync(HttpClient owner, Guid size, decimal? costEach, bool includesVat = true)
    {
        using var response = await owner.PutAsJsonAsync(Route($"/api/pot-sizes/{size}/allowance"), new { costEach, includesVat }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Json(await response.Content.ReadAsStringAsync(Cancellation));
    }

    private static async Task<Guid> PotOnAsync(HttpClient client, Guid tray, Guid batch, int quantity, Guid? potSizeId, DateTimeOffset at, Guid? id = null) =>
        Json(await PostForTextAsync(client, $"/api/containers/{tray}/transfer", new { eventId = id ?? Guid.NewGuid(), occurredAt = at, type = "PottedOn", batchId = batch, quantity, containerKind = "PotGroup", potSizeId, locationId = (Guid?)null })).GetProperty("eventId").GetGuid();

    private static (decimal WithVat, decimal BeforeVat) Rounded(JsonElement money) =>
        (Math.Round(money.GetProperty("withVat").GetDecimal(), 2), Math.Round(money.GetProperty("beforeVat").GetDecimal(), 2));

    [Fact]
    public async Task Potting_is_charged_at_the_allowance_of_its_day_and_a_first_amount_reaches_back()
    {
        var clock = new TestClock();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, clock: clock);
        var size = await AddPotSizeAsync(nursery.Owner, "9cm");
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = clock.GetUtcNow().AddDays(-5), plantId = await AddPlantAsync(nursery.Owner), quantity = 40, source = "SeedLot", originCountry = "GB", containerKind = "Tray", costAmount = 3m }));
        var (batch, tray) = (started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid());
        await PotOnAsync(nursery.Staff, tray, batch, 10, size, clock.GetUtcNow().AddDays(-3));

        // Nothing is charged until there's an amount, and the first one covers potting already recorded.
        Assert.Equal(["9cm"], (await CostingAsync(nursery.Owner, batch)).GetProperty("pots").GetProperty("withoutAllowance").EnumerateArray().Select(name => name.GetString()!), StringComparer.Ordinal);
        Assert.True((await SetAllowanceAsync(nursery.Owner, size, 0.42m)).GetProperty("first").GetBoolean());
        Assert.Equal((4.20m, 3.50m), Rounded((await CostingAsync(nursery.Owner, batch)).GetProperty("pots").GetProperty("cost")));

        // A new amount is used from its day; earlier potting keeps the amount it had.
        clock.Advance(TimeSpan.FromDays(2));
        var changed = await SetAllowanceAsync(nursery.Owner, size, 0.60m);
        Assert.False(changed.GetProperty("first").GetBoolean());
        Assert.False((await SetAllowanceAsync(nursery.Owner, size, 0.60m)).GetProperty("changed").GetBoolean());
        await PotOnAsync(nursery.Staff, tray, batch, 5, size, clock.GetUtcNow());

        var costing = await CostingAsync(nursery.Owner, batch);
        var lines = costing.GetProperty("pots").GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal([10, 5], lines.Select(line => line.GetProperty("plants").GetInt32()));
        Assert.Equal((7.20m, 6.00m), Rounded(costing.GetProperty("pots").GetProperty("cost")));
        Assert.Equal((10.20m, 8.50m), Rounded(costing.GetProperty("total")));
    }

    [Fact]
    public async Task Undone_potting_costs_nothing_and_potting_with_no_size_or_no_allowance_is_named()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var small = await AddPotSizeAsync(nursery.Owner, "9cm");
        var large = await AddPotSizeAsync(nursery.Owner, "2L");
        await SetAllowanceAsync(nursery.Owner, small, 0.42m);
        var (batch, tray) = await StartAsync(nursery.Staff, await AddPlantAsync(nursery.Owner));
        var undone = await PotOnAsync(nursery.Staff, tray, batch, 10, small, DateTimeOffset.UtcNow.AddMinutes(-3));
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/events/{undone}/correct", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow }));
        await PotOnAsync(nursery.Staff, tray, batch, 4, large, DateTimeOffset.UtcNow.AddMinutes(-2));
        await PotOnAsync(nursery.Staff, tray, batch, 3, null, DateTimeOffset.UtcNow.AddMinutes(-1));

        var pots = (await CostingAsync(nursery.Owner, batch)).GetProperty("pots");

        Assert.Empty(pots.GetProperty("lines").EnumerateArray());
        Assert.Equal(["2L"], pots.GetProperty("withoutAllowance").EnumerateArray().Select(name => name.GetString()!), StringComparer.Ordinal);
        Assert.Equal(3, pots.GetProperty("unsizedPlants").GetInt32());
    }

    [Fact]
    public async Task An_allowance_before_VAT_costs_more_with_VAT_and_only_the_owner_sees_any()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var size = await AddPotSizeAsync(nursery.Owner, "1L");
        await SetAllowanceAsync(nursery.Owner, size, 0.50m, includesVat: false);

        var allowance = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/pot-sizes/allowances"), Cancellation)).EnumerateArray().Single();
        Assert.Equal((0.60m, 0.50m), Rounded(allowance.GetProperty("each")));
        Assert.False(allowance.GetProperty("includesVat").GetBoolean());
        Assert.Equal(20m, allowance.GetProperty("vatRate").GetDecimal());

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/pot-sizes/allowances"));
        using (var staffSet = await nursery.Staff.PutAsJsonAsync(Route($"/api/pot-sizes/{size}/allowance"), new { costEach = 1m, includesVat = true }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.Forbidden, staffSet.StatusCode);
        }

        Assert.DoesNotContain("cost", await nursery.Staff.GetStringAsync(Route("/api/pot-sizes"), Cancellation), StringComparison.OrdinalIgnoreCase);

        using var tooDear = await nursery.Owner.PutAsJsonAsync(Route($"/api/pot-sizes/{size}/allowance"), new { costEach = 1000m, includesVat = true }, Cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, tooDear.StatusCode);

        // Taken away, nothing is charged from today.
        await SetAllowanceAsync(nursery.Owner, size, null);
        Assert.Equal(JsonValueKind.Null, (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/pot-sizes/allowances"), Cancellation))[0].GetProperty("costEach").ValueKind);

        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();
        await Assert.ThrowsAsync<PostgresException>(() => db.PotSizeCosts.ExecuteDeleteAsync(Cancellation));
    }

    private static async Task<(Guid Batch, Guid Tray)> StartedAtAsync(TwoPersonNursery nursery, DateTimeOffset at) =>
        Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = at, plantId = await AddPlantAsync(nursery.Owner), quantity = 100, source = "SeedLot", originCountry = "GB", containerKind = "Tray", costAmount = 3m })) is var started
            ? (started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid())
            : default;

    [Fact]
    public async Task A_first_amount_put_right_the_same_day_is_put_right_for_earlier_potting_too()
    {
        var clock = new TestClock();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, clock: clock);
        var size = await AddPotSizeAsync(nursery.Owner, "9cm");
        var (batch, tray) = await StartedAtAsync(nursery, clock.GetUtcNow().AddDays(-6));
        await PotOnAsync(nursery.Staff, tray, batch, 10, size, clock.GetUtcNow().AddDays(-3));

        Assert.True((await SetAllowanceAsync(nursery.Owner, size, 4.20m)).GetProperty("reachesBack").GetBoolean());
        var fixedUp = await SetAllowanceAsync(nursery.Owner, size, 0.42m);
        Assert.False(fixedUp.GetProperty("first").GetBoolean());
        Assert.True(fixedUp.GetProperty("reachesBack").GetBoolean());

        Assert.Equal((4.20m, 3.50m), Rounded((await CostingAsync(nursery.Owner, batch)).GetProperty("pots").GetProperty("cost")));

        // A day later it no longer reaches back.
        clock.Advance(TimeSpan.FromDays(1));
        Assert.False((await SetAllowanceAsync(nursery.Owner, size, 0.50m)).GetProperty("reachesBack").GetBoolean());
        Assert.Equal((4.20m, 3.50m), Rounded((await CostingAsync(nursery.Owner, batch)).GetProperty("pots").GetProperty("cost")));
    }

    [Fact]
    public async Task Amounts_taken_away_and_set_again_and_two_on_one_day_each_charge_their_own_period()
    {
        var clock = new TestClock();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, clock: clock);
        var size = await AddPotSizeAsync(nursery.Owner, "9cm");
        var (batch, tray) = await StartedAtAsync(nursery, clock.GetUtcNow().AddDays(-1));

        // Day one: two amounts, the later one counting; pricked out counts as potting too.
        await SetAllowanceAsync(nursery.Owner, size, 0.30m);
        await SetAllowanceAsync(nursery.Owner, size, 0.40m);
        Json(await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/transfer", new { eventId = Guid.NewGuid(), occurredAt = clock.GetUtcNow(), type = "PrickedOut", batchId = batch, quantity = 10, containerKind = "Tray", potSizeId = size, locationId = (Guid?)null }));

        // Day two: taken away.
        clock.Advance(TimeSpan.FromDays(1));
        await SetAllowanceAsync(nursery.Owner, size, null);
        await PotOnAsync(nursery.Staff, tray, batch, 10, size, clock.GetUtcNow());

        // Day three: set again.
        clock.Advance(TimeSpan.FromDays(1));
        await SetAllowanceAsync(nursery.Owner, size, 1.00m);
        await PotOnAsync(nursery.Staff, tray, batch, 10, size, clock.GetUtcNow());

        var pots = (await CostingAsync(nursery.Owner, batch)).GetProperty("pots");
        Assert.Equal((14.00m, 11.67m), Rounded(pots.GetProperty("cost")));
        Assert.Equal([10, 10], pots.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("plants").GetInt32()));
        Assert.Equal(["9cm"], pots.GetProperty("withoutAllowance").EnumerateArray().Select(name => name.GetString()!), StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_corrected_potting_is_charged_as_corrected_at_its_own_day()
    {
        var clock = new TestClock();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, clock: clock);
        var size = await AddPotSizeAsync(nursery.Owner, "9cm");
        var (batch, tray) = await StartedAtAsync(nursery, clock.GetUtcNow().AddDays(-6));
        await SetAllowanceAsync(nursery.Owner, size, 0.40m);
        var potting = await PotOnAsync(nursery.Staff, tray, batch, 10, size, clock.GetUtcNow());

        // The amount changes the next day, and then the potting is corrected to 6: still at its own day's amount.
        clock.Advance(TimeSpan.FromDays(1));
        await SetAllowanceAsync(nursery.Owner, size, 0.90m);
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Owner, $"/api/events/{potting}/correct", new { eventId = Guid.NewGuid(), occurredAt = clock.GetUtcNow(), quantity = 6 }));

        Assert.Equal((2.40m, 2.00m), Rounded((await CostingAsync(nursery.Owner, batch)).GetProperty("pots").GetProperty("cost")));
    }

    [Fact]
    public async Task The_same_amount_after_the_standard_rate_changes_is_brought_up_to_the_new_rate()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var size = await AddPotSizeAsync(nursery.Owner, "9cm");
        await SetAllowanceAsync(nursery.Owner, size, 0.42m);
        using (var rates = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/vat-rates"), new { standard = 25m, reduced = 5m }, Cancellation))
        {
            rates.EnsureSuccessStatusCode();
        }

        Assert.True((await SetAllowanceAsync(nursery.Owner, size, 0.42m)).GetProperty("changed").GetBoolean());

        var allowance = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/pot-sizes/allowances"), Cancellation))[0];
        Assert.Equal(25m, allowance.GetProperty("vatRate").GetDecimal());
        Assert.Equal((0.42m, 0.34m), Rounded(allowance.GetProperty("each")));
    }

    [Fact]
    public async Task Every_amount_set_comes_out_in_the_download_and_the_endpoints_are_off_with_costs()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var size = await AddPotSizeAsync(nursery.Owner, "9cm");
        await SetAllowanceAsync(nursery.Owner, size, 0.50m, includesVat: false);
        await SetAllowanceAsync(nursery.Owner, size, null);

        using (var response = await nursery.Owner.GetAsync(Route("/api/export"), Cancellation))
        {
            await using var zipped = await response.Content.ReadAsStreamAsync(Cancellation);
            using var archive = new System.IO.Compression.ZipArchive(zipped);
            using var sheet = new StreamReader(archive.GetEntry("pot-size-costs.csv")!.Open(), System.Text.Encoding.UTF8);
            var lines = (await sheet.ReadToEndAsync(Cancellation)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.EndsWith("Pot size,Amount typed,Includes VAT,VAT rate (%),With VAT,Before VAT,From,Set by,Set", lines[0], StringComparison.Ordinal);
            Assert.StartsWith("9cm,0.50,No,20.00,0.6000,0.50,", lines[1], StringComparison.Ordinal);
            Assert.StartsWith("9cm,Taken away,,20.00,,,", lines[2], StringComparison.Ordinal);
        }

        using (var off = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/features/Costs"), new { on = false }, Cancellation))
        {
            off.EnsureSuccessStatusCode();
        }

        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Owner, "/api/pot-sizes/allowances"));
        using var set = await nursery.Owner.PutAsJsonAsync(Route($"/api/pot-sizes/{size}/allowance"), new { costEach = 1m, includesVat = true }, Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, set.StatusCode);
    }

    private static async Task<Guid> LoseAsync(HttpClient client, Guid tray, Guid batch, int quantity, DateTimeOffset at, bool? all = null)
    {
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(client, $"/api/containers/{tray}/losses", new { eventId = id, occurredAt = at, batchId = batch, quantity, reason = "Disease", all }));
        return id;
    }

    private static async Task<Guid> CountAsync(HttpClient client, Guid tray, Guid batch, int counted, DateTimeOffset at)
    {
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(client, $"/api/containers/{tray}/count", new { eventId = id, occurredAt = at, batchId = batch, counted }));
        return id;
    }

    private static async Task CorrectAsync(HttpClient client, Guid work, int? quantity = null) =>
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(client, $"/api/events/{work}/correct", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, quantity }));

    private static async Task<JsonElement> PerPlantAsync(TwoPersonNursery nursery, Guid batch) =>
        (await CostingAsync(nursery.Owner, batch)).GetProperty("perPlant");

    private static async Task<(Guid Batch, Guid Tray)> SowAsync(TwoPersonNursery nursery, int quantity, decimal cost, DateTimeOffset at, string? sownAs = null) =>
        Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = at, plantId = await AddPlantAsync(nursery.Owner, $"Plant {Guid.NewGuid():N}"), quantity, source = "SeedLot", originCountry = "GB", containerKind = "Tray", costAmount = cost, sownAs })) is var started
            ? (started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid())
            : default;

    [Fact]
    public async Task The_plants_still_growing_carry_the_cost_and_a_loss_is_valued_at_what_had_been_spent_by_then()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var size = await AddPotSizeAsync(nursery.Owner, "9cm");
        await SetAllowanceAsync(nursery.Owner, size, 1.00m);
        var now = DateTimeOffset.UtcNow;
        var (batch, tray) = await SowAsync(nursery, 40, 4.80m, now.AddDays(-10));

        // 20 lost while only the seed had been spent on them: £4.80 over 40, so £2.40 between them.
        await LoseAsync(nursery.Staff, tray, batch, 20, now.AddDays(-8));
        // Then the other 20 are potted on at £1 each, which the lost ones never had.
        await PotOnAsync(nursery.Staff, tray, batch, 20, size, now.AddDays(-5));

        var perPlant = (await CostingAsync(nursery.Owner, batch)).GetProperty("perPlant");
        Assert.Equal(20, perPlant.GetProperty("alive").GetInt32());
        Assert.Equal(20, perPlant.GetProperty("lost").GetInt32());
        Assert.Equal((1.24m, 1.03m), Rounded(perPlant.GetProperty("each")));
        Assert.Equal((2.40m, 2.00m), Rounded(perPlant.GetProperty("lostValue")));
    }

    [Fact]
    public async Task A_tray_not_counted_yet_has_no_cost_for_each_plant_and_one_all_lost_lost_the_lot()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var now = DateTimeOffset.UtcNow;
        var (uncounted, _) = await SowAsync(nursery, 0, 3m, now.AddDays(-5), "Unknown");
        var (gone, goneTray) = await SowAsync(nursery, 10, 6m, now.AddDays(-5));
        await LoseAsync(nursery.Staff, goneTray, gone, 10, now.AddDays(-1));

        var notCounted = (await CostingAsync(nursery.Owner, uncounted)).GetProperty("perPlant");
        Assert.True(notCounted.GetProperty("notCounted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, notCounted.GetProperty("each").ValueKind);

        var allLost = (await CostingAsync(nursery.Owner, gone)).GetProperty("perPlant");
        Assert.Equal(0, allLost.GetProperty("alive").GetInt32());
        Assert.Equal(JsonValueKind.Null, allLost.GetProperty("each").ValueKind);
        Assert.Equal((6m, 5m), Rounded(allLost.GetProperty("lostValue")));
    }

    [Fact]
    public async Task Fewer_found_at_a_count_move_the_cost_for_each_plant_but_are_not_losses()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var now = DateTimeOffset.UtcNow;
        var (batch, tray) = await SowAsync(nursery, 40, 6.40m, now.AddDays(-5));
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/count", new { eventId = Guid.NewGuid(), occurredAt = now.AddDays(-1), batchId = batch, counted = 32 }));

        var perPlant = (await CostingAsync(nursery.Owner, batch)).GetProperty("perPlant");
        Assert.Equal(32, perPlant.GetProperty("alive").GetInt32());
        Assert.Equal(-8, perPlant.GetProperty("adjusted").GetInt32());
        Assert.Equal(0, perPlant.GetProperty("lost").GetInt32());
        Assert.Equal((0.20m, 0.17m), Rounded(perPlant.GetProperty("each")));
        Assert.Equal(JsonValueKind.Null, perPlant.GetProperty("lostValue").ValueKind);
    }

    [Fact]
    public async Task With_no_cost_known_there_is_nothing_for_each_plant()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, _) = await StartAsync(nursery.Staff, await AddPlantAsync(nursery.Owner));

        var perPlant = (await CostingAsync(nursery.Owner, batch)).GetProperty("perPlant");

        Assert.Equal(40, perPlant.GetProperty("alive").GetInt32());
        Assert.Equal(JsonValueKind.Null, perPlant.GetProperty("each").ValueKind);
    }

    private static async Task<(Guid Batch, Guid Tray, Guid Start)> SowWithStartAsync(TwoPersonNursery nursery, int quantity, decimal cost, DateTimeOffset at)
    {
        var start = Guid.NewGuid();
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = start, occurredAt = at, plantId = await AddPlantAsync(nursery.Owner, $"Plant {Guid.NewGuid():N}"), quantity, source = "SeedLot", originCountry = "GB", containerKind = "Tray", costAmount = cost }));
        return (started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid(), start);
    }

    [Fact]
    public async Task Two_losses_in_a_row_each_take_their_share_of_what_was_left()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var now = DateTimeOffset.UtcNow;
        var (batch, tray) = await SowAsync(nursery, 40, 4.80m, now.AddDays(-10));
        await LoseAsync(nursery.Staff, tray, batch, 10, now.AddDays(-8));
        await LoseAsync(nursery.Staff, tray, batch, 10, now.AddDays(-6));
        // Something else spent after the losses is never part of what they had cost.
        await AddCostAsync(nursery.Owner, batch, Cost(kind: "Other", amount: 12m, note: "Feed", occurredAt: now.AddDays(-2)));

        var perPlant = await PerPlantAsync(nursery, batch);
        Assert.Equal((2.40m, 2.00m), Rounded(perPlant.GetProperty("lostValue")));
        Assert.Equal((0.84m, 0.70m), Rounded(perPlant.GetProperty("each")));
    }

    [Fact]
    public async Task A_corrected_loss_or_start_is_walked_at_the_time_of_the_work_it_replaces()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var size = await AddPotSizeAsync(nursery.Owner, "9cm");
        await SetAllowanceAsync(nursery.Owner, size, 1.00m);
        var now = DateTimeOffset.UtcNow;

        var (batch, tray) = await SowAsync(nursery, 40, 4.80m, now.AddDays(-10));
        var loss = await LoseAsync(nursery.Staff, tray, batch, 20, now.AddDays(-8));
        await PotOnAsync(nursery.Staff, tray, batch, 20, size, now.AddDays(-5));
        await CorrectAsync(nursery.Owner, loss, 18);
        Assert.Equal((2.16m, 1.80m), Rounded((await PerPlantAsync(nursery, batch)).GetProperty("lostValue")));

        var (other, otherTray, start) = await SowWithStartAsync(nursery, 40, 4.80m, now.AddDays(-10));
        await LoseAsync(nursery.Staff, otherTray, other, 20, now.AddDays(-8));
        await CorrectAsync(nursery.Owner, start, 50);
        var corrected = await PerPlantAsync(nursery, other);
        Assert.Equal(30, corrected.GetProperty("alive").GetInt32());
        Assert.Equal((1.92m, 1.60m), Rounded(corrected.GetProperty("lostValue")));
    }

    [Fact]
    public async Task A_loss_that_arrives_late_from_a_phone_is_walked_with_what_the_count_absorbed()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var now = DateTimeOffset.UtcNow;
        var (batch, tray) = await SowAsync(nursery, 40, 4.80m, now.AddDays(-10));
        await CountAsync(nursery.Staff, tray, batch, 20, now.AddDays(-6));
        // Lost before the count, but only sent afterwards: the count had already seen it.
        await LoseAsync(nursery.Staff, tray, batch, 20, now.AddDays(-8));
        await LoseAsync(nursery.Staff, tray, batch, 10, now.AddDays(-4));

        var perPlant = await PerPlantAsync(nursery, batch);
        Assert.Equal(10, perPlant.GetProperty("alive").GetInt32());
        Assert.Equal((3.60m, 3.00m), Rounded(perPlant.GetProperty("lostValue")));
    }

    [Fact]
    public async Task One_batch_read_alone_agrees_with_Figures_when_a_count_is_undone()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var now = DateTimeOffset.UtcNow;
        var (batch, tray) = await SowAsync(nursery, 40, 4m, now.AddDays(-10));
        // A count that found what was expected has no line of its own.
        var count = await CountAsync(nursery.Staff, tray, batch, 40, now.AddDays(-6));
        await LoseAsync(nursery.Staff, tray, batch, 5, now.AddDays(-8));
        await CorrectAsync(nursery.Owner, count);

        var perPlant = await PerPlantAsync(nursery, batch);
        var figures = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/figures"), Cancellation)).GetProperty("plants")[0];
        Assert.Equal(figures.GetProperty("alive").GetInt32(), perPlant.GetProperty("alive").GetInt32());
        Assert.Equal(figures.GetProperty("adjusted").GetInt32(), perPlant.GetProperty("adjusted").GetInt32());
        Assert.Equal(35, perPlant.GetProperty("alive").GetInt32());
    }

    [Fact]
    public async Task A_tray_sown_without_a_count_and_all_lost_lost_its_whole_cost()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var now = DateTimeOffset.UtcNow;
        var (batch, tray) = await SowAsync(nursery, 0, 3m, now.AddDays(-10), "Unknown");
        await LoseAsync(nursery.Staff, tray, batch, 0, now.AddDays(-2), all: true);

        var perPlant = await PerPlantAsync(nursery, batch);
        Assert.False(perPlant.GetProperty("notCounted").GetBoolean());
        Assert.Equal((3.00m, 2.50m), Rounded(perPlant.GetProperty("lostValue")));
    }

    [Fact]
    public async Task Plants_gone_at_a_count_are_not_a_loss_and_more_found_share_the_cost()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var now = DateTimeOffset.UtcNow;

        // Counted down to nought, as when plants leave by the till: nothing was lost.
        var (gone, goneTray) = await SowAsync(nursery, 40, 4m, now.AddDays(-10));
        await CountAsync(nursery.Staff, goneTray, gone, 0, now.AddDays(-2));
        Assert.Equal(JsonValueKind.Null, (await PerPlantAsync(nursery, gone)).GetProperty("lostValue").ValueKind);

        // Ten lost, the rest gone at a count: only the ten are valued as lost.
        var (mixed, mixedTray) = await SowAsync(nursery, 40, 4.80m, now.AddDays(-10));
        await LoseAsync(nursery.Staff, mixedTray, mixed, 10, now.AddDays(-5));
        await CountAsync(nursery.Staff, mixedTray, mixed, 0, now.AddDays(-2));
        Assert.Equal((1.20m, 1.00m), Rounded((await PerPlantAsync(nursery, mixed)).GetProperty("lostValue")));

        var (more, moreTray) = await SowAsync(nursery, 40, 4.50m, now.AddDays(-10));
        await CountAsync(nursery.Staff, moreTray, more, 45, now.AddDays(-2));
        var found = await PerPlantAsync(nursery, more);
        Assert.Equal(5, found.GetProperty("adjusted").GetInt32());
        Assert.Equal((0.10m, 0.08m), Rounded(found.GetProperty("each")));
    }

    private static async Task<(HttpStatusCode Status, string? Problem, Guid Id)> SellAsync(HttpClient client, Guid tray, Guid batch, int quantity, decimal? priceEach, DateTimeOffset? at = null)
    {
        var id = Guid.NewGuid();
        using var response = await client.PostAsJsonAsync(Route($"/api/containers/{tray}/sold"), new { eventId = id, occurredAt = at ?? DateTimeOffset.UtcNow, batchId = batch, quantity, priceEach }, Cancellation);
        var text = await response.Content.ReadAsStringAsync(Cancellation);
        return (response.StatusCode, response.StatusCode == HttpStatusCode.Conflict ? Json(text).GetProperty("problem").GetString() : null, id);
    }

    private static async Task<int> HereAsync(HttpClient client, Guid tray) =>
        (await client.GetFromJsonAsync<JsonElement>(Route("/api/containers/active"), Cancellation)).EnumerateArray()
            .Single(container => container.GetProperty("id").GetGuid() == tray).GetProperty("contents")[0].GetProperty("quantity").GetInt32();

    private static async Task<decimal?> SaleVatRateAsync(TwoPersonNursery nursery, Guid sale)
    {
        await using var scope = nursery.App.Services.CreateAsyncScope();
        var payload = await scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>().Events.Where(e => e.Id == sale).Select(e => e.Payload).SingleAsync(Cancellation);
        return Json(payload).GetProperty("vatRate").GetDecimal();
    }

    [Fact]
    public async Task A_sale_from_a_tray_takes_the_plants_off_it_and_can_be_undone_unlike_an_invoices()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, tray) = await SowAsync(nursery, 20, 3m, DateTimeOffset.UtcNow.AddDays(-10));

        var (status, _, sale) = await SellAsync(nursery.Staff, tray, batch, 3, 4.50m);
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(17, await HereAsync(nursery.Staff, tray));
        Assert.Equal((HttpStatusCode.Conflict, "BelowZero"), (await SellAsync(nursery.Staff, tray, batch, 50, 4.50m)) is var tooMany ? (tooMany.Status, tooMany.Problem) : default);

        await CorrectAsync(nursery.Staff, sale);
        Assert.Equal(20, await HereAsync(nursery.Staff, tray));

        // An invoice's sale is undone only by a credit note.
        var customer = Json(await PostForTextAsync(nursery.Owner, "/api/customers", new { name = "Hilltop" })).GetProperty("id").GetGuid();
        var invoice = Json(await PostForTextAsync(nursery.Owner, "/api/invoices", new { customerId = customer, lines = new[] { new { description = "Lavender", quantity = 2, unitPrice = 5m, vatRate = 0m, plantId = (Guid?)null, containerId = tray, batchId = batch } } }));
        await using var scope = nursery.App.Services.CreateAsyncScope();
        var invoiceSale = await scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>().Invoices.Where(row => row.Id == invoice.GetProperty("id").GetGuid()).Select(row => row.SoldEventId).SingleAsync(Cancellation);
        using var undo = await nursery.Owner.PostAsJsonAsync(Route($"/api/events/{invoiceSale}/correct"), new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow }, Cancellation);
        Assert.Equal("SoldByInvoice", Json(await undo.Content.ReadAsStringAsync(Cancellation)).GetProperty("problem").GetString());
    }

    [Fact]
    public async Task A_tray_not_counted_yet_is_counted_before_a_sale()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, tray) = await SowAsync(nursery, 0, 3m, DateTimeOffset.UtcNow.AddDays(-10), "Unknown");

        Assert.Equal((HttpStatusCode.Conflict, "NotCounted"), (await SellAsync(nursery.Staff, tray, batch, 1, 4m)) is var refused ? (refused.Status, refused.Problem) : default);
        Assert.Equal(HttpStatusCode.BadRequest, (await SellAsync(nursery.Staff, tray, batch, 1, -1m)).Status);
    }

    [Fact]
    public async Task A_sale_keeps_the_VAT_it_was_charged_at()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner, "Basil", "Reduced");
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddDays(-10), plantId = plant, quantity = 30, source = "SeedLot", originCountry = "GB", containerKind = "Tray" }));
        var (batch, tray) = (started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid());

        // While not registered, nothing is charged.
        var unregistered = (await SellAsync(nursery.Staff, tray, batch, 1, 3m)).Id;
        Assert.Equal(0m, await SaleVatRateAsync(nursery, unregistered));

        using (var registered = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { name = "Croft View", vatNumber = "GB123456789", hasLogo = false, vatRegistered = true, paymentTermsDays = 14 }, Cancellation))
        {
            Assert.True(registered.IsSuccessStatusCode);
        }

        var afterwards = (await SellAsync(nursery.Staff, tray, batch, 1, 3m)).Id;
        Assert.Equal(5m, await SaleVatRateAsync(nursery, afterwards));

        // A later change of rate leaves it as it was.
        using (var rates = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/vat-rates"), new { standard = 20m, reduced = 8m }, Cancellation))
        {
            rates.EnsureSuccessStatusCode();
        }

        Assert.Equal(5m, await SaleVatRateAsync(nursery, afterwards));
    }

    [Fact]
    public async Task The_costing_counts_what_was_taken_on_invoices_and_from_trays()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, tray) = await SowAsync(nursery, 20, 3m, DateTimeOffset.UtcNow.AddDays(-10));
        await SellAsync(nursery.Staff, tray, batch, 4, 4.50m);
        await SellAsync(nursery.Staff, tray, batch, 2, null);
        var undone = (await SellAsync(nursery.Staff, tray, batch, 5, 9m)).Id;
        await CorrectAsync(nursery.Staff, undone);
        var customer = Json(await PostForTextAsync(nursery.Owner, "/api/customers", new { name = "Hilltop" })).GetProperty("id").GetGuid();
        await PostForTextAsync(nursery.Owner, "/api/invoices", new { customerId = customer, lines = new[] { new { description = "Lavender", quantity = 6, unitPrice = 3.50m, vatRate = 0m, plantId = (Guid?)null, containerId = tray, batchId = batch } } });

        var takings = (await CostingAsync(nursery.Owner, batch)).GetProperty("takings");
        Assert.Equal(6, takings.GetProperty("invoiced").GetInt32());
        Assert.Equal((21m, 21m), Rounded(takings.GetProperty("invoiceTakings")));
        Assert.Equal(4, takings.GetProperty("fromTrays").GetInt32());
        Assert.Equal((18m, 18m), Rounded(takings.GetProperty("trayTakings")));
        Assert.Equal(2, takings.GetProperty("noPrice").GetInt32());
        Assert.Equal(12, takings.GetProperty("sold").GetInt32());
        Assert.Equal((39m, 39m), Rounded(takings.GetProperty("total")));
    }

    [Fact]
    public async Task Costs_are_never_changed_or_deleted()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, _) = await StartAsync(nursery.Owner, await AddPlantAsync(nursery.Owner));
        await AddCostAsync(nursery.Owner, batch, Cost());
        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();

        var update = await Assert.ThrowsAsync<PostgresException>(() => db.BatchCosts.ExecuteUpdateAsync(setters => setters.SetProperty(cost => cost.AmountPaid, 1m), Cancellation));
        Assert.Contains("append-only", update.MessageText, StringComparison.Ordinal);
        await Assert.ThrowsAsync<PostgresException>(() => db.BatchCosts.ExecuteDeleteAsync(Cancellation));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE batch_costs", Cancellation));
    }
}
