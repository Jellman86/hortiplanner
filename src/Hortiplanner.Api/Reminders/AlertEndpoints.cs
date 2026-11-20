using System.Globalization;
using System.Security.Claims;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Production;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Reminders;

/// <summary>
/// Things that need a look, worked out from the records each time, so each says why it is there and goes once it's
/// dealt with. Nothing is stored and nothing needs dismissing. Each person gets their own: staff what is theirs to do,
/// such as jobs and counting, and never anything about customers or money.
/// </summary>
internal static class AlertEndpoints
{
    private const int RenewalWarningDays = 42;
    private const int ReadyTooLongDays = 28;
    private const int UncountedTooLongDays = 21;
    private const int JobLateDays = 2;

    // The backup jobs run hourly and report on each other's schedule, as on Data and backups.
    private static readonly TimeSpan BackupLimit = TimeSpan.FromMinutes(150);

    public static void MapAlertEndpoints(this IEndpointRouteBuilder app) => app.MapGet("/api/alerts", GetAsync);

    private static Task<List<Alert>> GetAsync(ClaimsPrincipal user, HortiplannerDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken) =>
        ListAsync(db, store, clock, AccountServices.IsOwner(user), cancellationToken);

    private static string Day(DateOnly day) => day.ToString("ddd d MMM yyyy", CultureInfo.GetCultureInfo("en-GB"));

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";

    /// <summary>
    /// The list in order: problems first, then things with a date, then jobs, then trays. The owner's includes the
    /// nursery-wide items; staff get jobs due and seedlings to count.
    /// </summary>
    internal static async Task<List<Alert>> ListAsync(HortiplannerDbContext db, EventStore store, TimeProvider clock, bool forOwner, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var alerts = new List<Alert>();

        // Nothing is raised about a part of Hortiplanner the owner has turned off.
        var off = (await db.GetNurserySettingsAsync(cancellationToken)).FeaturesOff;

        if (forOwner)
        {
            alerts.AddIfAny(await BackupsAsync(db, clock, cancellationToken));
            alerts.AddIfAny(off.Contains(NurseryFeature.Passports) ? null : await RenewalAsync(db, today, cancellationToken));
            alerts.AddIfAny(off.Contains(NurseryFeature.Invoices) ? null : await OverdueInvoicesAsync(db, today, cancellationToken));
        }

        if (!off.Contains(NurseryFeature.Jobs))
        {
            alerts.AddIfAny(forOwner ? await LateJobsAsync(db, today, cancellationToken) : null);
            alerts.AddIfAny(await DueJobsAsync(db, today, cancellationToken));
        }

        var trays = await TraysAsync(db, store, clock, cancellationToken);
        alerts.AddRange(trays.Where(alert => forOwner || string.Equals(alert.Kind, "NotCountedLong", StringComparison.Ordinal)));

        return alerts;
    }

    private static void AddIfAny(this List<Alert> alerts, Alert? alert)
    {
        if (alert is not null)
        {
            alerts.Add(alert);
        }
    }

    private static async Task<Alert?> BackupsAsync(HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var status = await db.BackupStatus.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var now = clock.GetUtcNow();

        if (status is null)
        {
            // A new nursery has a day's grace before the backups are expected to report.
            var firstAccount = await db.Users.MinAsync(person => (DateTimeOffset?)person.CreatedAt, cancellationToken);
            return firstAccount is null || now - firstAccount < TimeSpan.FromHours(24)
                ? null
                : new Alert(
                    "BackupsStopped",
                    "Hortiplanner has never heard from the backups",
                    "Please check that the nursery's records are being backed up.",
                    "data",
                    1,
                    "Problem");
        }

        return status.LastDumpAt >= now - BackupLimit && status.LastTransferAt >= now - BackupLimit
            ? null
            : new Alert(
                "BackupsStopped",
                "The backups may have stopped",
                "The nursery's records aren't being protected until this is put right.",
                "data",
                1,
                "Problem");
    }

    private static async Task<Alert?> LateJobsAsync(HortiplannerDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var late = await db.Jobs.CountAsync(job => job.ArchivedAt == null && job.NextDue < today.AddDays(-JobLateDays), cancellationToken);

        return late == 0
            ? null
            : new Alert(
                "JobsLate",
                $"{Plural(late, "job is", "jobs are")} running late",
                late == 1
                    ? $"It was due more than {JobLateDays} days ago and nobody has ticked it off yet."
                    : $"Each one was due more than {JobLateDays} days ago and hasn't been ticked off yet.",
                "jobs",
                late,
                "Soon");
    }

    private static async Task<Alert?> DueJobsAsync(HortiplannerDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var due = await db.Jobs.CountAsync(job => job.ArchivedAt == null && job.NextDue <= today, cancellationToken);

        return due == 0
            ? null
            : new Alert(
                "JobsDue",
                due == 1 ? "1 job is due today" : $"{due} jobs are due",
                due == 1
                    ? "It's on Record work, ready to tick off once it's done."
                    : "They're on Record work, ready to tick off as each one is done.",
                "record",
                due,
                "Tidy");
    }

    private static async Task<Alert?> RenewalAsync(HortiplannerDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var renews = (await db.GetNurserySettingsAsync(cancellationToken)).AuthorisationRenewsOn;

        if (renews is not { } date || date > today.AddDays(RenewalWarningDays))
        {
            return null;
        }

        var days = date.DayNumber - today.DayNumber;
        var why = days < 0
            ? $"It was due on {Day(date)}. Once you've renewed it, add the next renewal date in Nursery details."
            : $"It's due on {Day(date)}, which is in {Plural(days, "day", "days")}. Book the inspection, and once it's renewed, add the next date in Nursery details.";

        return new Alert("Authorisation", "Your plant health authorisation needs renewing", why, "nursery", 1, days < 0 ? "Problem" : "Soon");
    }

    private static async Task<Alert?> OverdueInvoicesAsync(HortiplannerDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var overdue = await db.Invoices
            .Where(invoice => invoice.Kind == InvoiceKind.Invoice
                && invoice.PaidOn == null
                && invoice.DueOn != null
                && invoice.DueOn < today
                && !db.Invoices.Any(credit => credit.CreditsInvoiceId == invoice.Id))
            .Select(invoice => invoice.DueOn!.Value)
            .ToListAsync(cancellationToken);

        return overdue.Count == 0
            ? null
            : new Alert(
                "InvoicesOverdue",
                $"{Plural(overdue.Count, "invoice is", "invoices are")} overdue",
                overdue.Count == 1
                    ? $"It hasn't been marked as paid, and it was due on {Day(overdue.Min())}."
                    : $"They haven't been marked as paid. The oldest was due on {Day(overdue.Min())}.",
                "invoices",
                overdue.Count,
                "Soon");
    }

    /// <summary>Seedlings up for weeks that nobody has counted, then trays ready for a while, which may need selling or potting on.</summary>
    private static async Task<List<Alert>> TraysAsync(HortiplannerDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        var stock = await store.GetStockAsync(cancellationToken);
        var stages = await ProductionEndpoints.FindStagesAsync([.. stock.Select(row => row.ContainerId).Distinct()], db, cancellationToken);
        var now = clock.GetUtcNow();
        var alerts = new List<Alert>();

        // Seed still waiting to come up has nothing to count yet: what was sown is its record until then.
        var uncounted = stock
            .Where(row => row.NotCounted)
            .Select(row => row.ContainerId)
            .Distinct()
            .Count(id => stages.TryGetValue(id, out var stage) && stage.Stage != ProductionStage.Sown && now - stage.Since > TimeSpan.FromDays(UncountedTooLongDays));

        if (uncounted > 0)
        {
            alerts.Add(new Alert(
                "NotCountedLong",
                $"{Plural(uncounted, "tray of seedlings has", "trays of seedlings have")} gone three weeks without a count",
                "We know what was sown, but not how many plants came up. Counting them when you prick them out, or now if you have a minute, keeps the figures accurate.",
                "production",
                uncounted,
                "Tidy"));
        }

        var ready = stages.Count(stage => stage.Value.Stage == ProductionStage.Ready && now - stage.Value.Since > TimeSpan.FromDays(ReadyTooLongDays));

        if (ready > 0)
        {
            alerts.Add(new Alert(
                "ReadyTooLong",
                $"{Plural(ready, "tray has", "trays have")} been ready for over four weeks",
                ready == 1
                    ? "It was marked ready for sale more than four weeks ago. It might need selling, potting on, or a look at its price."
                    : "They were marked ready for sale more than four weeks ago. They might need selling, potting on, or a look at their prices.",
                "production",
                ready,
                "Tidy"));
        }

        return alerts;
    }

    /// <param name="Kind">What sort of thing it is, so a screen can choose an icon.</param>
    /// <param name="Why">Why it appeared, in words.</param>
    /// <param name="Where">Where to deal with it: "record", "data", "nursery", "invoices", "production" or "jobs".</param>
    /// <param name="Count">How many are behind the title, such as three overdue invoices.</param>
    /// <param name="Level">"Problem" (red), "Soon" (amber) or "Tidy" (blue), for the card's coloured edge.</param>
    internal sealed record Alert(string Kind, string Title, string Why, string Where, int Count, string Level);
}
