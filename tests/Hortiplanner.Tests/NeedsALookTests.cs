using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hortiplanner.Api.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

/// <summary>"Needs a look" for everyone: each person their own list, worked out afresh, in a fixed order.</summary>
public sealed class NeedsALookTests(PostgresFixture database)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

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
        Assert.Equal("1 job is due today", one.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Staff_never_get_the_owners_items()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        using var settings = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { name = "Croft View", hasLogo = false, vatRegistered = false, paymentTermsDays = 30, authorisationRenewsOn = Today.AddDays(-1), setsRenewal = true }, Cancellation);
        settings.EnsureSuccessStatusCode();
        await ReportBackupsAsync(nursery, TimeSpan.FromHours(5));
        await PostForTextAsync(nursery.Owner, "/api/jobs", new { title = "Spray roses", everyDays = 14, firstDue = Today.AddDays(-5) });

        Assert.Equal(["BackupsStopped", "Authorisation", "JobsLate", "JobsDue"], Kinds(await AlertsAsync(nursery.Owner)));
        Assert.Equal(["JobsDue"], Kinds(await AlertsAsync(nursery.Staff)));
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
    public async Task A_new_nursery_has_a_days_grace_before_silent_backups_are_raised()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        Assert.DoesNotContain("BackupsStopped", Kinds(await AlertsAsync(nursery.Owner)), StringComparer.Ordinal);
    }
}
