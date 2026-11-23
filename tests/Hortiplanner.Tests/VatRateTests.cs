using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

/// <summary>VAT rates under Settings: a history kept for good, each change from its day on.</summary>
public sealed class VatRateTests(PostgresFixture database)
{
    private const string Path = "/api/settings/vat-rates";

    private static Task<JsonElement> RatesAsync(HttpClient owner) =>
        owner.GetFromJsonAsync<JsonElement>(Route(Path), Cancellation);

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SaveAsync(HttpClient owner, decimal standard, decimal reduced)
    {
        using var response = await owner.PutAsJsonAsync(Route(Path), new { standard, reduced }, Cancellation);
        return (response.StatusCode, Json(await response.Content.ReadAsStringAsync(Cancellation)));
    }

    [Fact]
    public async Task The_rates_start_at_20_and_5_and_a_change_adds_one_row_from_today()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var rates = await RatesAsync(nursery.Owner);
        Assert.Equal(20m, rates.GetProperty("standard").GetDecimal());
        Assert.Equal(5m, rates.GetProperty("reduced").GetDecimal());
        Assert.Equal(2, rates.GetProperty("changes").GetArrayLength());

        var (status, saved) = await SaveAsync(nursery.Owner, 17.5m, 5m);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Standard", Assert.Single(saved.GetProperty("changed").EnumerateArray()).GetString());

        rates = await RatesAsync(nursery.Owner);
        Assert.Equal(17.5m, rates.GetProperty("standard").GetDecimal());
        var latest = rates.GetProperty("changes").EnumerateArray().Last();
        Assert.Equal("Standard", latest.GetProperty("band").GetString());
        Assert.False(string.IsNullOrEmpty(latest.GetProperty("setBy").GetString()));

        // The same figures again change nothing.
        (status, saved) = await SaveAsync(nursery.Owner, 17.5m, 5m);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(saved.GetProperty("changed").EnumerateArray());
        Assert.Equal(3, (await RatesAsync(nursery.Owner)).GetProperty("changes").GetArrayLength());
    }

    [Fact]
    public async Task Only_real_rates_are_taken_and_only_from_the_owner()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        Assert.Equal("Rate", (await SaveAsync(nursery.Owner, 120m, 5m)).Body.GetProperty("problem").GetString());
        Assert.Equal("Rate", (await SaveAsync(nursery.Owner, 20m, -1m)).Body.GetProperty("problem").GetString());
        Assert.Equal("Rate", (await SaveAsync(nursery.Owner, 20.125m, 5m)).Body.GetProperty("problem").GetString());
        Assert.Equal("ReducedAboveStandard", (await SaveAsync(nursery.Owner, 5m, 20m)).Body.GetProperty("problem").GetString());
        using var missing = await nursery.Owner.PutAsJsonAsync(Route(Path), new { standard = 20m }, Cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, Path));
        using var staffSave = await nursery.Staff.PutAsJsonAsync(Route(Path), new { standard = 10m, reduced = 5m }, Cancellation);
        Assert.Equal(HttpStatusCode.Forbidden, staffSave.StatusCode);
        Assert.Equal(20m, (await RatesAsync(nursery.Owner)).GetProperty("standard").GetDecimal());
    }

    [Fact]
    public async Task A_rate_is_the_newest_starting_on_or_before_a_day_and_the_later_of_two_on_one_day()
    {
        var clock = new TestClock();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, clock: clock);
        await SaveAsync(nursery.Owner, 18m, 5m);
        clock.Advance(TimeSpan.FromMinutes(5));
        await SaveAsync(nursery.Owner, 19m, 5m);
        clock.Advance(TimeSpan.FromDays(3));
        await SaveAsync(nursery.Owner, 21m, 5m);

        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        Assert.Equal(21m, await Api.Settings.VatRates.PercentAsync(db, VatBand.Standard, today, Cancellation));
        Assert.Equal(19m, await Api.Settings.VatRates.PercentAsync(db, VatBand.Standard, today.AddDays(-1), Cancellation));
        Assert.Equal(20m, await Api.Settings.VatRates.PercentAsync(db, VatBand.Standard, today.AddDays(-30), Cancellation));
        Assert.Equal(20m, await Api.Settings.VatRates.PercentAsync(db, VatBand.Standard, new DateOnly(1990, 1, 1), Cancellation));
        Assert.Equal(0m, await Api.Settings.VatRates.PercentAsync(db, VatBand.Zero, today, Cancellation));
    }

    [Fact]
    public async Task Overlapping_saves_keep_one_valid_pair_even_on_a_fixed_clock()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database, clock: new TestClock());
        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();
        await using var blocker = await db.Database.BeginTransactionAsync(Cancellation);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE vat_rate_changes IN SHARE MODE", Cancellation);
        var lower = SaveAsync(nursery.Owner, 6m, 5m);
        var higher = SaveAsync(nursery.Owner, 20m, 10m);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::integer AS "Value" FROM pg_locks
            WHERE relation = 'vat_rate_changes'::regclass AND database = (SELECT oid FROM pg_database WHERE datname = current_database()) AND NOT granted
            """).SingleAsync(deadline.Token) < 2)
        {
            await Task.Delay(10, deadline.Token);
        }

        await blocker.CommitAsync(Cancellation);
        Assert.All(await Task.WhenAll(lower, higher), reply => Assert.Equal(HttpStatusCode.OK, reply.Status));
        var rates = await RatesAsync(nursery.Owner);
        var actual = (rates.GetProperty("standard").GetDecimal(), rates.GetProperty("reduced").GetDecimal());
        Assert.Contains(actual, new[] { (6m, 5m), (20m, 10m) });
    }

    [Fact]
    public async Task The_history_is_never_changed_or_deleted()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();

        var update = await Assert.ThrowsAsync<PostgresException>(() => db.VatRateChanges.ExecuteUpdateAsync(setters => setters.SetProperty(change => change.Percent, 1m), Cancellation));
        Assert.Contains("append-only", update.MessageText, StringComparison.Ordinal);
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.VatRateChanges.ExecuteDeleteAsync(Cancellation));
        Assert.Contains("append-only", delete.MessageText, StringComparison.Ordinal);
        var truncate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE vat_rate_changes", Cancellation));
        Assert.Contains("append-only", truncate.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Starting_afresh_keeps_the_rates()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database, demo: true);
        await SaveAsync(nursery.Owner, 17.5m, 5m);
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, "/api/settings/clear-records", new { confirmation = "clear everything" }));

        Assert.Equal(17.5m, (await RatesAsync(nursery.Owner)).GetProperty("standard").GetDecimal());
    }
}
