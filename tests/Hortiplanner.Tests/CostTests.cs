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
        await AddCostAsync(nursery.Owner, batch, Cost(answer: "Free", amount: null));
        var costing = await CostingAsync(nursery.Owner, batch);
        Assert.Equal("Free", costing.GetProperty("material").GetProperty("state").GetString());
        Assert.Equal((0m, 0m), Pair(costing.GetProperty("total")));

        await AddCostAsync(nursery.Owner, batch, Cost(answer: "NotKnown", amount: null));
        costing = await CostingAsync(nursery.Owner, batch);
        Assert.Equal("NotKnown", costing.GetProperty("material").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, costing.GetProperty("total").ValueKind);
        Assert.False(await CostMissingAsync(nursery.Staff, tray));

        var history = costing.GetProperty("history").EnumerateArray().ToList();
        Assert.Equal(["Amount", "Free", "NotKnown"], history.Select(entry => entry.GetProperty("answer").GetString()!), StringComparer.Ordinal);
        Assert.Equal([true, true, false], history.Select(entry => entry.GetProperty("replaced").GetBoolean()));
        Assert.Equal(history[0].GetProperty("id").GetGuid(), history[1].GetProperty("replacesId").GetGuid());

        // Removed, it goes back to having no answer, and the tray asks again.
        Assert.Equal(HttpStatusCode.Created, await AddCostAsync(nursery.Owner, batch, Cost(answer: "Removed", amount: null)));
        Assert.Equal("NoAnswer", (await CostingAsync(nursery.Owner, batch)).GetProperty("material").GetProperty("state").GetString());
        Assert.True(await CostMissingAsync(nursery.Staff, tray));
        Assert.Equal(HttpStatusCode.Conflict, await AddCostAsync(nursery.Owner, batch, Cost(answer: "Removed", amount: null)));
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
        Assert.Equal(HttpStatusCode.Forbidden, await AddCostAsync(nursery.Staff, batch, Cost(answer: "Removed", amount: null)));
        Assert.Equal(HttpStatusCode.Forbidden, await AddCostAsync(nursery.Staff, batch, Cost(vatBand: "Zero", includesVat: true)));
        Assert.Equal(HttpStatusCode.Forbidden, await AddCostAsync(nursery.Staff, batch, Cost(kind: "Other", amount: 1m, note: "Feed", replacesId: Guid.NewGuid())));
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

        Assert.Equal(HttpStatusCode.Created, await AddCostAsync(nursery.Owner, batch, Cost(amount: 2.49m, occurredAt: at, vatBand: "Standard", includesVat: false)));

        var costing = await CostingAsync(nursery.Owner, batch);
        Assert.Equal((2.99m, 2.49m), Pair(costing.GetProperty("material").GetProperty("cost")));
        Assert.True(costing.GetProperty("material").GetProperty("entry").GetProperty("vatSaid").GetBoolean());
        Assert.Equal(2, costing.GetProperty("history").GetArrayLength());

        await AddCostAsync(nursery.Owner, batch, Cost(amount: 2.49m, occurredAt: at, vatBand: "Zero", includesVat: true));
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
