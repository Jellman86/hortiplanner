using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Lookup;

/// <summary>
/// Fills in the family tree of plants matched to a species before family trees were kept, at start-up and then daily,
/// one at a time and gently, stopping quietly when the online sources can't be reached.
/// </summary>
internal sealed partial class LineageBackfill(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock, ILogger<LineageBackfill> logger) : BackgroundService
{
    private static readonly TimeSpan FirstWait = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Between = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Every = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Species:LineageBackfill", defaultValue: true))
        {
            return;
        }

        try
        {
            await Task.Delay(FirstWait, clock, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Nothing here may stop Hortiplanner: a failed run is noted and tried again tomorrow.
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    LogFailed(logger, exception.GetType().Name);
                }

                await Task.Delay(Every, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping with the application.
        }
    }

    /// <summary>Each species a plant is matched to that has no family tree yet. Returns how many were filled in.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Species:Online", defaultValue: true))
        {
            return 0;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();
        var sources = scope.ServiceProvider.GetRequiredService<SpeciesSources>();
        var waiting = await db.Species
            .Where(species => species.TaxonId == null && db.Plants.Any(plant => plant.SpeciesId == species.Id))
            .OrderBy(species => species.Id)
            .ToListAsync(cancellationToken);
        var filled = 0;

        foreach (var species in waiting)
        {
            try
            {
                if (await SpeciesLineage.EnsureAsync(species, db, sources, configuration, clock, cancellationToken))
                {
                    break;
                }
            }
            catch (Exception exception) when (exception is DbUpdateException or HttpRequestException or System.Text.Json.JsonException)
            {
                LogFailed(logger, exception.GetType().Name);
                break;
            }

            filled += species.TaxonId is null ? 0 : 1;
            await Task.Delay(Between, clock, cancellationToken);
        }

        return filled;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Filling in family trees stopped: {Kind}")]
    private static partial void LogFailed(ILogger logger, string kind);
}
