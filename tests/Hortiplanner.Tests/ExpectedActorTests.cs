using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class ExpectedActorTests(PostgresFixture database)
{
    [Fact]
    public async Task Queued_work_is_only_written_for_the_expected_actor()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Thyme" }));
        var eventId = Guid.NewGuid();
        var work = new { eventId, occurredAt = DateTimeOffset.UtcNow, plantId = plant.GetProperty("id").GetGuid(), quantity = 10, source = "SeedLot", originCountry = "GB", containerKind = "Tray" };
        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();

        foreach (var expectedActor in new[] { (await nursery.OwnerIdAsync()).ToString(), "invalid" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Route("/api/batches")) { Content = JsonContent.Create(work) };
            request.Headers.Add("X-Expected-Actor", expectedActor);
            using var response = await nursery.Staff.SendAsync(request, Cancellation);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("actor_mismatch", (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("problem").GetString());
            Assert.False(await db.Events.AnyAsync(entry => entry.Id == eventId, Cancellation));
        }

        using var matching = new HttpRequestMessage(HttpMethod.Post, Route("/api/batches")) { Content = JsonContent.Create(work) };
        matching.Headers.Add("X-Expected-Actor", nursery.StaffId.ToString());
        using var accepted = await nursery.Staff.SendAsync(matching, Cancellation);

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(nursery.StaffId, (await db.Events.SingleAsync(entry => entry.Id == eventId, Cancellation)).RecordedBy);
    }
}
