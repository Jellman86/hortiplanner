using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Hortiplanner.Tests;

public sealed class ReviewMigrationTests(PostgresFixture database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Legacy_counts_are_backfilled_without_changing_the_ledger_and_absorb_late_work()
    {
        await using var db = await database.CreateMigratedDatabaseAsync();
        var person = new AppUser { Id = Guid.CreateVersion7(), UserName = "Test worker" };
        var plant = new Plant { CommonName = "Lavender" };
        var batch = new Batch { PlantId = plant.Id, TraceabilityCode = "B1", Source = BatchSource.SeedLot, OriginCountry = "GB" };
        var tray = new Container { LabelCode = "C1", Kind = ContainerKind.Tray };
        db.AddRange(person, plant, batch, tray);
        await db.SaveChangesAsync(Cancellation);
        var start = DateTimeOffset.UtcNow.AddDays(-2);
        Event Work(EventType type, DateTimeOffset at, int change)
        {
            var work = new Event { Id = Guid.CreateVersion7(), Type = type, OccurredAt = at, ContainerId = tray.Id, RecordedBy = person.Id };
            work.Lines.Add(new EventLine { BatchId = batch.Id, ContainerId = tray.Id, QuantityChange = change });
            return work;
        }
        var store = new EventStore(db, TimeProvider.System);
        Assert.Equal(AppendResult.Stored, await store.AppendAsync(Work(EventType.Sown, start, 40), Cancellation));
        var count = Work(EventType.StockCounted, start.AddHours(2), -5);
        Assert.Equal(AppendResult.Stored, await store.AppendAsync(count, Cancellation));
        await db.GetService<IMigrator>().MigrateAsync("20261112192133_InvoicedConsignments", Cancellation);
        await db.Database.MigrateAsync(Cancellation);
        db.ChangeTracker.Clear();
        Assert.Equal(35, (await db.Set<LegacyCount>().SingleAsync(Cancellation)).Counted);
        Assert.Equal("{}", (await db.Events.SingleAsync(e => e.Id == count.Id, Cancellation)).Payload);
        Assert.Equal(AppendResult.Stored, await store.AppendAsync(Work(EventType.Lost, start.AddHours(1), -3), Cancellation));
        Assert.Equal(35, Assert.Single((await store.GetContainerStateAsync(tray.Id, Cancellation)).Contents).Quantity);
    }

    [Fact]
    public async Task A_plants_single_picture_comes_through_as_its_main_photo()
    {
        await using var db = await database.CreateMigratedDatabaseAsync();
        var plant = new Plant { CommonName = "Lavender" };
        var other = new Plant { CommonName = "Foxglove" };
        db.AddRange(plant, other);
        await db.SaveChangesAsync(Cancellation);
        await db.GetService<IMigrator>().MigrateAsync("20261114101014_PersonPicture", Cancellation);
        byte[] picture = [0xFF, 0xD8, 0xFF, 7];
        await db.Database.ExecuteSqlAsync($"INSERT INTO plant_photos (plant_id, content_type, content) VALUES ({plant.Id}, 'image/jpeg', {picture}), ({other.Id}, 'image/jpeg', {picture})", Cancellation);

        await db.Database.MigrateAsync(Cancellation);
        db.ChangeTracker.Clear();

        // Each comes through as its plant's main photo, with an id of its own.
        Assert.Equal(2, (await db.PlantPhotos.Select(row => row.Id).Distinct().ToListAsync(Cancellation)).Count);
        Assert.Equal(2, await db.PlantPhotos.CountAsync(row => row.IsMain, Cancellation));
        var photo = await db.PlantPhotos.SingleAsync(row => row.PlantId == plant.Id, Cancellation);
        Assert.Equal(plant.Id, photo.PlantId);
        Assert.True(photo.IsMain);
        Assert.Equal(PlantPhotoKind.Other, photo.Kind);
        Assert.NotEqual(Guid.Empty, photo.Id);
        Assert.Null(photo.UploadedBy);
        Assert.Equal(picture, photo.Content);

        // A photo added since isn't the main one unless it says so.
        db.PlantPhotos.Add(new PlantPhoto { PlantId = plant.Id, ContentType = "image/jpeg", Content = picture });
        await db.SaveChangesAsync(Cancellation);
        Assert.Equal(1, await db.PlantPhotos.CountAsync(row => row.PlantId == plant.Id && row.IsMain, Cancellation));

        // Going back keeps each plant's main photo as its one picture.
        await db.GetService<IMigrator>().MigrateAsync("20261114101014_PersonPicture", Cancellation);
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM plant_photos").SingleAsync(Cancellation));
        await db.Database.MigrateAsync(Cancellation);
    }
}
