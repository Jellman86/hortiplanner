using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

/// <summary>"Needs a look" for everyone: each person their own list, worked out afresh, in a fixed order.</summary>
public sealed class NeedsALookTests(PostgresFixture database)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    private static async Task<Guid> AddAsync(HttpClient client, string path, object body) =>
        Json(await PostForTextAsync(client, path, body)).GetProperty("id").GetGuid();

    private static async Task<List<JsonElement>> AlertsAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<JsonElement>(Route("/api/alerts"), Cancellation)).EnumerateArray()];

    private static List<string> Kinds(List<JsonElement> alerts) => [.. alerts.Select(alert => alert.GetProperty("kind").GetString()!)];

    private static async Task ReportBackupsAsync(TwoPersonNursery nursery, TimeSpan age)
    {
        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();
        var status = await db.BackupStatus.FindAsync([1], Cancellation);

        if (status is null)
        {
            status = new BackupStatus();
            db.BackupStatus.Add(status);
        }

        status.LastDumpAt = DateTimeOffset.UtcNow - age;
        status.LastTransferAt = DateTimeOffset.UtcNow - age;
        await db.SaveChangesAsync(Cancellation);
    }

    [Fact]
    public async Task Jobs_due_appear_for_everyone_with_how_many_and_go_once_ticked_off()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        Assert.Empty(await AlertsAsync(nursery.Staff));

        var created = Json(await PostForTextAsync(nursery.Owner, "/api/jobs", new { title = "Water house 1", everyDays = 7, firstDue = Today }));
        await PostForTextAsync(nursery.Owner, "/api/jobs", new { title = "Feed the tunnel", everyDays = 14, firstDue = Today.AddDays(-1) });

        var due = Assert.Single(await AlertsAsync(nursery.Staff));
        Assert.Equal("JobsDue", due.GetProperty("kind").GetString());
        Assert.Equal(2, due.GetProperty("count").GetInt32());
        Assert.Equal("2 jobs are due", due.GetProperty("title").GetString());
        Assert.Equal("record", due.GetProperty("where").GetString());

        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/jobs/{created.GetProperty("id").GetGuid()}/done", new { }));
        var one = Assert.Single(await AlertsAsync(nursery.Staff));
        Assert.Equal("1 job is due", one.GetProperty("title").GetString());
    }

    /// <summary>An invoice due today, with no days to pay, which is overdue once the clock moves on.</summary>
    private static async Task InvoiceDueTodayAsync(TwoPersonNursery nursery)
    {
        var customer = await AddAsync(nursery.Owner, "/api/customers", new { name = "Hilltop", address = "1 High Street", email = "", phone = "", vatNumber = "" });
        using var issued = await PostAsync(nursery.Owner, "/api/invoices", new { customerId = customer, notes = "", lines = new[] { new { description = "Lavender 2L", quantity = 1, unitPrice = 5m, vatRate = 0m } } });
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
    }

    /// <summary>Seedlings up for over three weeks that nobody has counted, and a tray ready for over four weeks.</summary>
    private static async Task TraysAsync(TwoPersonNursery nursery)
    {
        var plant = await AddAsync(nursery.Owner, "/api/plants", new { commonName = "Primula", xylellaHost = false, protectedVariety = false });
        var sown = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddDays(-40), plantId = plant, quantity = 0, source = "SeedLot", originCountry = "GB", containerKind = "Tray", sownAs = "About", sownAmount = 200 }));
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/containers/{sown.GetProperty("containerId").GetGuid()}/germinated", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddDays(-22) }));

        var counted = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddDays(-40), plantId = plant, quantity = 20, source = "SeedLot", originCountry = "GB", containerKind = "Tray" }));
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/containers/{counted.GetProperty("containerId").GetGuid()}/ready", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddDays(-30) }));
    }

    [Fact]
    public async Task The_owner_gets_every_kind_in_order_and_staff_get_only_theirs()
    {
        var clock = new TestClock();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, clock: clock);
        using var settings = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { name = "Croft View", hasLogo = false, vatRegistered = false, paymentTermsDays = 0, authorisationRenewsOn = Today.AddDays(-1), setsRenewal = true }, Cancellation);
        settings.EnsureSuccessStatusCode();
        await ReportBackupsAsync(nursery, TimeSpan.FromHours(5));
        await PostForTextAsync(nursery.Owner, "/api/jobs", new { title = "Spray roses", everyDays = 14, firstDue = Today.AddDays(-5) });
        await InvoiceDueTodayAsync(nursery);
        await TraysAsync(nursery);
        clock.Advance(TimeSpan.FromDays(2));

        Assert.Equal(
            ["BackupsStopped", "Authorisation", "InvoicesOverdue", "JobsLate", "JobsDue", "NotCountedLong", "ReadyTooLong"],
            Kinds(await AlertsAsync(nursery.Owner)));
        Assert.Equal(["JobsDue", "NotCountedLong"], Kinds(await AlertsAsync(nursery.Staff)));
    }

    [Fact]
    public async Task Stopped_backups_are_a_problem_until_they_report_again()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await ReportBackupsAsync(nursery, TimeSpan.FromHours(5));

        var backups = Assert.Single(await AlertsAsync(nursery.Owner));
        Assert.Equal("BackupsStopped", backups.GetProperty("kind").GetString());
        Assert.Equal("Problem", backups.GetProperty("level").GetString());
        Assert.Equal("data", backups.GetProperty("where").GetString());

        await ReportBackupsAsync(nursery, TimeSpan.FromMinutes(5));
        Assert.Empty(await AlertsAsync(nursery.Owner));
    }

    [Fact]
    public async Task Backups_that_never_reported_are_raised_after_a_new_nurserys_first_day()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        Assert.DoesNotContain("BackupsStopped", Kinds(await AlertsAsync(nursery.Owner)), StringComparer.Ordinal);

        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();
        await db.Users.ExecuteUpdateAsync(setters => setters.SetProperty(person => person.CreatedAt, DateTimeOffset.UtcNow.AddDays(-2)), Cancellation);

        var never = Assert.Single(await AlertsAsync(nursery.Owner));
        Assert.Equal("Hortiplanner has never heard from the backups", never.GetProperty("title").GetString());
        Assert.Equal("Problem", never.GetProperty("level").GetString());
    }
}
