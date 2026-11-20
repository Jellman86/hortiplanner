using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;
using static Hortiplanner.Tests.AssistantTests;

namespace Hortiplanner.Tests;

/// <summary>Reading a seed packet photo: only the photo is sent, what's read comes back to check, and nothing is saved.</summary>
public sealed class ReadLabelTests(PostgresFixture database)
{
    private const string Photo = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private static async Task<Guid> AddAsync(HttpClient client, string path, object body) =>
        Json(await PostForTextAsync(client, path, body)).GetProperty("id").GetGuid();

    private static async Task<TwoPersonNursery> SetUpAsync(PostgresFixture database, PretendOpenRouter openRouter)
    {
        var nursery = await TwoPersonNursery.CreateAsync(database, outsideWorld: openRouter);
        using var saved = await nursery.Owner.PutAsJsonAsync(Route("/api/assistant/settings"), new { key = Key, model = "test/model", forStaff = false }, Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        return nursery;
    }

    private static Task<HttpResponseMessage> ReadAsync(HttpClient client, string image = Photo) =>
        client.PostAsJsonAsync(Route("/api/assistant/read-label"), new { kind = "SeedPacket", image }, Cancellation);

    private static async Task<int> SpendsAsync(TwoPersonNursery nursery)
    {
        await using var scope = nursery.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>().AssistantSpends.CountAsync(Cancellation);
    }

    [Fact]
    public async Task A_seed_packet_is_read_from_the_photo_alone_and_matched_to_the_nurserys_plants_and_supplier()
    {
        var year = DateTime.UtcNow.Year + 1;
        var openRouter = new PretendOpenRouter
        {
            Label = $$"""
                {"what":"seed_packet","commonName":"SWEET PEA","variety":"'Cupani'","botanicalName":"lathyrus odoratus","supplierName":"chiltern seeds",
                 "supplierPassportNumber":"gb-12 345","lotOrReference":"L2291\u0007","sowByMonth":"2","sowByYear":{{year}},"contentsAsPrinted":"approx. 25 seeds",
                 "seedCount":25,"unsure":["lotOrReference","madeUp"]}
                """,
        };
        await using var nursery = await SetUpAsync(database, openRouter);
        var supplier = await AddAsync(nursery.Owner, "/api/suppliers", new { name = "Chiltern Seeds", passportRegistrationNumber = "GB 12345" });
        var plant = await AddAsync(nursery.Owner, "/api/plants", new { commonName = "Sweet pea", variety = "Cupani", botanicalName = "Lathyrus odoratus", xylellaHost = false, protectedVariety = false });
        await AddAsync(nursery.Owner, "/api/plants", new { commonName = "Sweet pea", variety = "Matucana", botanicalName = "Lathyrus odoratus", xylellaHost = false, protectedVariety = false });
        var before = await SpendsAsync(nursery);

        using var response = await ReadAsync(nursery.Owner);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = Json(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.True(read.GetProperty("readable").GetBoolean());
        Assert.Equal("Cupani", read.GetProperty("variety").GetString());
        Assert.Equal("L2291", read.GetProperty("lotOrReference").GetString());
        Assert.Equal($"{year}-02-{DateTime.DaysInMonth(year, 2):00}", read.GetProperty("sowBy").GetString());
        Assert.Equal(25, read.GetProperty("seedCount").GetInt32());
        Assert.Equal(["lotOrReference"], read.GetProperty("unsure").EnumerateArray().Select(field => field.GetString()), StringComparer.Ordinal);
        Assert.Equal(supplier, read.GetProperty("supplierId").GetGuid());
        // Both sweet peas match, whatever the capitals, the variety on the packet first.
        Assert.Equal(plant, read.GetProperty("plantMatches")[0].GetProperty("id").GetGuid());
        Assert.Equal(2, read.GetProperty("plantMatches").GetArrayLength());
        Assert.Equal(0.0005m, read.GetProperty("cost").GetDecimal());
        Assert.Equal(before + 1, await SpendsAsync(nursery));

        // One question holding the photo, the reading forced through the tool, and nothing from the records.
        var asked = JsonNode.Parse(Assert.Single(openRouter.Questions).Body)!;
        Assert.Equal("deny", asked["provider"]!["data_collection"]!.GetValue<string>());
        Assert.Equal("read_seed_packet", asked["tool_choice"]!["function"]!["name"]!.GetValue<string>());
        Assert.Equal(Photo, asked["messages"]![1]!["content"]![1]!["image_url"]!["url"]!.GetValue<string>());
        Assert.DoesNotContain("Chiltern", openRouter.Questions[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Matucana", openRouter.Questions[0].Body, StringComparison.Ordinal);

        // Nothing was added: an unknown supplier comes back as a name only.
        openRouter.Label = """{"what":"seed_packet","commonName":"Cosmos","supplierName":"Somewhere New Seeds","sowByYear":1999,"sowByMonth":4}""";
        using var unknown = await ReadAsync(nursery.Owner);
        var other = Json(await unknown.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal("Somewhere New Seeds", other.GetProperty("supplierName").GetString());
        Assert.Equal(JsonValueKind.Null, other.GetProperty("supplierId").ValueKind);
        Assert.Equal(JsonValueKind.Null, other.GetProperty("sowBy").ValueKind);
        Assert.Empty(other.GetProperty("plantMatches").EnumerateArray());
        Assert.Single((await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/suppliers"), Cancellation)).EnumerateArray());
    }

    [Theory]
    [InlineData("""{"what":"seed_packet","commonName":"Cosmos","price":2.99,"priceAsPrinted":"£2.99"}""", "2.99", "£2.99")]
    [InlineData("""{"what":"seed_packet","commonName":"Cosmos","price":"£3.50"}""", "3.50", null)]
    [InlineData("""{"what":"seed_packet","commonName":"Cosmos","price":3.5,"priceAsPrinted":"€3.50"}""", null, "€3.50")]
    [InlineData("""{"what":"seed_packet","commonName":"Cosmos","price":2.999}""", null, null)]
    [InlineData("""{"what":"seed_packet","commonName":"Cosmos","price":1250}""", null, null)]
    [InlineData("""{"what":"seed_packet","commonName":"Cosmos","price":0}""", null, null)]
    [InlineData("""{"what":"seed_packet","commonName":"Cosmos"}""", null, null)]
    public async Task A_price_is_taken_only_when_printed_in_pounds_and_sensible(string label, string? price, string? asPrinted)
    {
        await using var nursery = await SetUpAsync(database, new PretendOpenRouter { Label = label });

        var read = Json(await (await ReadAsync(nursery.Owner)).Content.ReadAsStringAsync(Cancellation));

        Assert.Equal(price, read.GetProperty("price").ValueKind == JsonValueKind.Null ? null : read.GetProperty("price").GetDecimal().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(asPrinted, read.GetProperty("priceAsPrinted").GetString());
    }

    [Fact]
    public async Task A_price_alone_on_a_photo_that_isnt_a_packet_fills_nothing_in()
    {
        await using var nursery = await SetUpAsync(database, new PretendOpenRouter { Label = """{"what":"other","price":4.99,"priceAsPrinted":"£4.99"}""" });

        var read = Json(await (await ReadAsync(nursery.Owner)).Content.ReadAsStringAsync(Cancellation));

        Assert.False(read.GetProperty("readable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("price").ValueKind);
    }

    [Fact]
    public async Task The_model_is_asked_for_the_price_as_printed()
    {
        var openRouter = new PretendOpenRouter { Label = """{"what":"seed_packet","commonName":"Cosmos"}""" };
        await using var nursery = await SetUpAsync(database, openRouter);
        await ReadAsync(nursery.Owner);

        var properties = JsonNode.Parse(Assert.Single(openRouter.Questions).Body)!["tools"]![0]!["function"]!["parameters"]!["properties"]!;
        Assert.Equal("number", properties["price"]!["type"]!.GetValue<string>());
        Assert.Equal("string", properties["priceAsPrinted"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_photo_that_isnt_a_packet_or_cant_be_read_fills_nothing_in_but_is_still_counted()
    {
        var openRouter = new PretendOpenRouter { Label = """{"what":"other","commonName":"A cat"}""" };
        await using var nursery = await SetUpAsync(database, openRouter);

        var other = Json(await (await ReadAsync(nursery.Owner)).Content.ReadAsStringAsync(Cancellation));
        Assert.False(other.GetProperty("readable").GetBoolean());
        Assert.Equal("other", other.GetProperty("what").GetString());
        Assert.Equal(JsonValueKind.Null, other.GetProperty("commonName").ValueKind);

        openRouter.Label = """{"what":"seed_packet"}""";
        var empty = Json(await (await ReadAsync(nursery.Owner)).Content.ReadAsStringAsync(Cancellation));
        Assert.False(empty.GetProperty("readable").GetBoolean());
        Assert.Equal("unreadable", empty.GetProperty("what").GetString());

        // A model that writes its reading out instead of using the tool is still understood.
        openRouter.Mode = "label as text";
        openRouter.Label = """{"what":"seed_packet","commonName":"Nasturtium"}""";
        var written = Json(await (await ReadAsync(nursery.Owner)).Content.ReadAsStringAsync(Cancellation));
        Assert.Equal("Nasturtium", written.GetProperty("commonName").GetString());

        openRouter.Mode = "garbage";
        using var garbage = await ReadAsync(nursery.Owner);
        Assert.Equal(HttpStatusCode.Conflict, garbage.StatusCode);
        Assert.Equal("Unavailable", Json(await garbage.Content.ReadAsStringAsync(Cancellation)).GetProperty("problem").GetString());
        Assert.Equal(4, await SpendsAsync(nursery));
    }

    [Fact]
    public async Task Reading_a_label_is_refused_like_a_question_and_only_photos_are_taken()
    {
        var openRouter = new PretendOpenRouter { Label = """{"what":"seed_packet","commonName":"Cosmos"}""" };
        await using var nursery = await TwoPersonNursery.CreateAsync(database, outsideWorld: openRouter);

        using (var notSetUp = await ReadAsync(nursery.Owner))
        {
            Assert.Equal(HttpStatusCode.Conflict, notSetUp.StatusCode);
            Assert.Equal("NotSetUp", Json(await notSetUp.Content.ReadAsStringAsync(Cancellation)).GetProperty("problem").GetString());
        }

        using (var saved = await nursery.Owner.PutAsJsonAsync(Route("/api/assistant/settings"), new { key = Key, model = "test/model", forStaff = false }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }

        using (var staff = await ReadAsync(nursery.Staff))
        {
            Assert.Equal(HttpStatusCode.Forbidden, staff.StatusCode);
        }

        foreach (var image in (string[])["data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=", "https://example.com/a.jpg", string.Empty])
        {
            using var refused = await ReadAsync(nursery.Owner, image);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        Assert.Empty(openRouter.Questions);
    }
}
