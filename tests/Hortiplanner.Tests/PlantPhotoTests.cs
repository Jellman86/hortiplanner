using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hortiplanner.Api.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class PlantPhotoTests(PostgresFixture database)
{
    private static byte[] Jpeg(byte marker) => [0xFF, 0xD8, 0xFF, 0xE0, marker];

    private static async Task<Guid> AddPlantAsync(HttpClient client) =>
        Json(await PostForTextAsync(client, "/api/plants", new { commonName = $"Foxglove {Guid.NewGuid():N}", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();

    private static async Task<HttpResponseMessage> AddAsync(HttpClient client, Guid plant, byte[] body, string kind = "Flower")
    {
        using var content = new ByteArrayContent(body);
        return await client.PostAsync(Route($"/api/plants/{plant}/photos?kind={kind}"), content, Cancellation);
    }

    private static async Task<Guid> AddIdAsync(HttpClient client, Guid plant, byte marker, string kind = "Flower")
    {
        using var response = await AddAsync(client, plant, Jpeg(marker), kind);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Json(await response.Content.ReadAsStringAsync(Cancellation)).GetProperty("id").GetGuid();
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, Guid plant) =>
        [.. (await client.GetFromJsonAsync<JsonElement>(Route($"/api/plants/{plant}/photos"), Cancellation)).EnumerateArray()];

    private static async Task<byte[]> MainAsync(HttpClient client, Guid plant) =>
        await client.GetByteArrayAsync(Route($"/api/plants/{plant}/photo"), Cancellation);

    private static async Task<HttpStatusCode> DeleteAsync(HttpClient client, Guid photo)
    {
        using var response = await client.DeleteAsync(Route($"/api/plant-photos/{photo}"), Cancellation);
        return response.StatusCode;
    }

    [Fact]
    public async Task A_plant_keeps_several_photos_each_with_a_kind_and_caption_and_one_main_picture()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);

        var flower = await AddIdAsync(nursery.Staff, plant, 1, "Flower");
        var foliage = await AddIdAsync(nursery.Staff, plant, 2, "Foliage");

        // The first is the main picture; the rest wait their turn.
        var photos = await ListAsync(nursery.Staff, plant);
        Assert.Equal([flower, foliage], photos.Select(photo => photo.GetProperty("id").GetGuid()));
        Assert.True(photos[0].GetProperty("isMain").GetBoolean());
        Assert.False(photos[1].GetProperty("isMain").GetBoolean());
        Assert.Equal("Foliage", photos[1].GetProperty("kind").GetString());
        Assert.True(photos[1].GetProperty("canBeAsked").GetBoolean());
        Assert.Equal(Jpeg(1), await MainAsync(nursery.Staff, plant));

        using (var described = await nursery.Staff.PutAsJsonAsync(Route($"/api/plant-photos/{foliage}"), new { kind = "Habit", caption = "  In its second summer  " }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.OK, described.StatusCode);
        }

        using (var main = await nursery.Staff.PutAsync(Route($"/api/plant-photos/{foliage}/main"), null, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, main.StatusCode);
        }

        photos = await ListAsync(nursery.Staff, plant);
        Assert.Equal(foliage, photos[0].GetProperty("id").GetGuid());
        Assert.True(photos[0].GetProperty("isMain").GetBoolean());
        Assert.Equal("Habit", photos[0].GetProperty("kind").GetString());
        Assert.Equal("In its second summer", photos[0].GetProperty("caption").GetString());
        Assert.Single(photos, photo => photo.GetProperty("isMain").GetBoolean());
        Assert.Equal(Jpeg(2), await MainAsync(nursery.Staff, plant));

        using var read = await nursery.Staff.GetAsync(Route($"/api/plant-photos/{flower}"), Cancellation);
        Assert.Equal(Jpeg(1), await read.Content.ReadAsByteArrayAsync(Cancellation));

        var listed = (await nursery.Staff.GetFromJsonAsync<JsonElement>(Route("/api/plants"), Cancellation)).EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == plant);
        Assert.True(listed.GetProperty("hasPhoto").GetBoolean());
    }

    [Fact]
    public async Task Removing_the_main_picture_hands_it_to_the_newest_and_only_the_uploader_or_owner_can_remove()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        var owners = await AddIdAsync(nursery.Owner, plant, 1);
        var older = await AddIdAsync(nursery.Staff, plant, 2);
        var newer = await AddIdAsync(nursery.Staff, plant, 3);

        // Staff can't remove the owner's photo, but can remove their own; the owner can remove anyone's.
        Assert.Equal(HttpStatusCode.Forbidden, await DeleteAsync(nursery.Staff, owners));
        Assert.False((await ListAsync(nursery.Staff, plant)).Single(photo => photo.GetProperty("id").GetGuid() == owners).GetProperty("canRemove").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, await DeleteAsync(nursery.Owner, owners));

        var photos = await ListAsync(nursery.Staff, plant);
        Assert.Equal(newer, photos[0].GetProperty("id").GetGuid());
        Assert.True(photos[0].GetProperty("isMain").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, await DeleteAsync(nursery.Staff, newer));
        Assert.Equal(HttpStatusCode.NoContent, await DeleteAsync(nursery.Staff, older));
        Assert.Empty(await ListAsync(nursery.Staff, plant));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Staff, $"/api/plants/{plant}/photo"));
        Assert.Equal(HttpStatusCode.NotFound, await DeleteAsync(nursery.Owner, older));
    }

    [Fact]
    public async Task New_batch_adds_the_plants_picture_as_main_only_when_it_has_none()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);

        using (var first = new ByteArrayContent(Jpeg(1)))
        using (var put = await nursery.Staff.PutAsync(Route($"/api/plants/{plant}/photo"), first, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using (var second = new ByteArrayContent(Jpeg(2)))
        using (var put = await nursery.Staff.PutAsync(Route($"/api/plants/{plant}/photo"), second, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        // Nothing is written over: both are kept, and the first stays the main picture.
        var photos = await ListAsync(nursery.Staff, plant);
        Assert.Equal(2, photos.Count);
        Assert.All(photos, photo => Assert.Equal("Other", photo.GetProperty("kind").GetString()));
        Assert.Equal(Jpeg(1), await MainAsync(nursery.Staff, plant));
    }

    [Fact]
    public async Task Bad_photos_kinds_captions_and_too_many_photos_are_refused()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);

        using (var notAPhoto = await AddAsync(nursery.Staff, plant, [1, 2, 3]))
        {
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, notAPhoto.StatusCode);
        }

        using (var tooBig = await AddAsync(nursery.Staff, plant, [0xFF, 0xD8, 0xFF, .. new byte[8 * 1024 * 1024]]))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode);
        }

        using (var badKind = await AddAsync(nursery.Staff, plant, Jpeg(1), kind: "Root"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, badKind.StatusCode);
        }

        using (var noPlant = await AddAsync(nursery.Staff, Guid.CreateVersion7(), Jpeg(1)))
        {
            Assert.Equal(HttpStatusCode.NotFound, noPlant.StatusCode);
        }

        var photo = await AddIdAsync(nursery.Staff, plant, 1);
        using (var longCaption = await nursery.Staff.PutAsJsonAsync(Route($"/api/plant-photos/{photo}"), new { kind = "Flower", caption = new string('a', 201) }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.BadRequest, longCaption.StatusCode);
        }

        for (var added = 1; added < 30; added++)
        {
            await AddIdAsync(nursery.Staff, plant, (byte)added);
        }

        using var tooMany = await AddAsync(nursery.Staff, plant, Jpeg(99));
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);
    }

    [Fact]
    public async Task A_plants_page_shows_photos_from_its_batches_unless_batch_photos_are_off()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.CreateVersion7(), occurredAt = DateTimeOffset.UtcNow, plantId = plant, quantity = 10, source = "SeedLot", originCountry = "GB", containerKind = "Tray" }));
        var batch = started.GetProperty("batchId").GetGuid();

        foreach (var kind in new[] { "Growing", "Packet" })
        {
            using var content = new ByteArrayContent(Jpeg(1));
            using var response = await nursery.Staff.PostAsync(Route($"/api/batches/{batch}/photos?kind={kind}"), content, Cancellation);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        // Only photos of the plants growing: a packet says nothing about what the plant looks like.
        var fromBatches = (await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/plants/{plant}/batch-photos"), Cancellation)).EnumerateArray().ToList();
        var shown = Assert.Single(fromBatches);
        Assert.Equal(started.GetProperty("traceabilityCode").GetString(), shown.GetProperty("traceabilityCode").GetString());

        using (var off = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/features/BatchPhotos"), new { on = false }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        }

        Assert.NotEqual(HttpStatusCode.OK, await GetStatusAsync(nursery.Staff, $"/api/plants/{plant}/batch-photos"));

        // The plant's own photos don't depend on batch photos being on.
        await AddIdAsync(nursery.Staff, plant, 5);
        Assert.Single(await ListAsync(nursery.Staff, plant));
    }

    [Fact]
    public async Task Photos_added_or_chosen_at_the_same_moment_still_leave_exactly_one_main_picture()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);

        var added = await Task.WhenAll(Enumerable.Range(1, 6).Select(marker => AddAsync(marker % 2 == 0 ? nursery.Owner : nursery.Staff, plant, Jpeg((byte)marker))));
        Assert.All(added, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        foreach (var response in added)
        {
            response.Dispose();
        }

        var photos = await ListAsync(nursery.Staff, plant);
        Assert.Single(photos, photo => photo.GetProperty("isMain").GetBoolean());

        var chosen = await Task.WhenAll(photos.Select(async photo =>
        {
            using var response = await nursery.Staff.PutAsync(Route($"/api/plant-photos/{photo.GetProperty("id").GetGuid()}/main"), null, Cancellation);
            return response.StatusCode;
        }));
        Assert.All(chosen, status => Assert.Equal(HttpStatusCode.NoContent, status));
        Assert.Single(await ListAsync(nursery.Staff, plant), photo => photo.GetProperty("isMain").GetBoolean());
    }

    [Fact]
    public async Task A_picture_from_before_photos_were_kept_in_numbers_is_removed_only_by_the_owner()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        await using (var scope = nursery.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();
            db.PlantPhotos.Add(new PlantPhoto { PlantId = plant, IsMain = true, ContentType = "image/jpeg", Content = Jpeg(1) });
            await db.SaveChangesAsync(Cancellation);
        }

        var old = Assert.Single(await ListAsync(nursery.Staff, plant));
        Assert.False(old.GetProperty("canRemove").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, await DeleteAsync(nursery.Staff, old.GetProperty("id").GetGuid()));
        Assert.True(Assert.Single(await ListAsync(nursery.Owner, plant)).GetProperty("canRemove").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, await DeleteAsync(nursery.Owner, old.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task A_photo_too_big_for_ask_hortiplanner_is_kept_but_not_offered_to_it_and_unknown_photos_are_not_found()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        using (var big = await AddAsync(nursery.Staff, plant, [0xFF, 0xD8, 0xFF, .. new byte[(3 * 1024 * 1024)]]))
        {
            Assert.Equal(HttpStatusCode.Created, big.StatusCode);
            Assert.False(Json(await big.Content.ReadAsStringAsync(Cancellation)).GetProperty("canBeAsked").GetBoolean());
        }

        Assert.False(Assert.Single(await ListAsync(nursery.Staff, plant)).GetProperty("canBeAsked").GetBoolean());

        var unknown = Guid.CreateVersion7();
        using (var main = await nursery.Staff.PutAsync(Route($"/api/plant-photos/{unknown}/main"), null, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NotFound, main.StatusCode);
        }

        using var described = await nursery.Staff.PutAsJsonAsync(Route($"/api/plant-photos/{unknown}"), new { kind = "Flower", caption = "Hello" }, Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, described.StatusCode);
    }
}
