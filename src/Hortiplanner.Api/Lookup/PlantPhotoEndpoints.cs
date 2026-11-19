using System.Security.Claims;
using Hortiplanner.Api.Assistant;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Lookup;

/// <summary>The nursery's own photos of each plant (flower, foliage, the whole plant, its label), one of them its main picture.</summary>
internal static class PlantPhotoEndpoints
{
    /// <summary>The most photos one plant keeps. Photos live in the database, so every one is in every backup.</summary>
    internal const int MostPhotos = 30;

    private const int MostBatchPhotos = 24;

    public static void MapPlantPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/plants/{id:guid}/photos", ListAsync);
        app.MapPost("/api/plants/{id:guid}/photos", AddAsync).Produces<PlantPhotoSummary>(StatusCodes.Status201Created);
        app.MapGet("/api/plants/{id:guid}/batch-photos", ListBatchPhotosAsync).RequireFeature(NurseryFeature.BatchPhotos);
        app.MapGet("/api/plant-photos/{id:guid}", GetAsync);
        app.MapPut("/api/plant-photos/{id:guid}", DescribeAsync).Produces<PlantPhotoSummary>();
        app.MapPut("/api/plant-photos/{id:guid}/main", MakeMainAsync);
        app.MapDelete("/api/plant-photos/{id:guid}", RemoveAsync);

        // Kept for New batch's photo of the plant itself: it adds a photo, which becomes the main picture only if the
        // plant has none.
        app.MapPut("/api/plants/{id:guid}/photo", AddFromNewBatchAsync);
        app.MapGet("/api/plants/{id:guid}/photo", GetMainAsync);
    }

    /// <summary>A plant's photos, its main picture first, then the newest.</summary>
    private static async Task<List<PlantPhotoSummary>> ListAsync(Guid id, ClaimsPrincipal user, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var photos = await db.PlantPhotos
            .Where(photo => photo.PlantId == id)
            .OrderByDescending(photo => photo.IsMain)
            .ThenByDescending(photo => photo.CreatedAt)
            .ThenByDescending(photo => photo.Id)
            .Select(photo => new { photo.Id, photo.Kind, photo.Caption, photo.IsMain, photo.CreatedAt, photo.UploadedBy, Size = photo.Content.Length })
            .ToListAsync(cancellationToken);

        return photos.ConvertAll(photo => new PlantPhotoSummary(
            photo.Id,
            photo.Kind,
            photo.Caption,
            photo.IsMain,
            photo.CreatedAt,
            PhotoEndpoints.MayRemove(user, photo.UploadedBy),
            photo.Size <= AssistantEndpoints.LargestImage));
    }

    private static async Task<IResult> AddAsync(Guid id, PlantPhotoKind? kind, HttpRequest request, ClaimsPrincipal user, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await AddPhotoAsync(id, kind ?? PlantPhotoKind.Other, request, user, db, cancellationToken) switch
        {
            (null, var refusal) => TypedResults.StatusCode(refusal),
            ({ } photo, _) => TypedResults.Created($"/api/plant-photos/{photo.Id}", Summary(photo, canRemove: true)),
        };

    private static async Task<IResult> AddFromNewBatchAsync(Guid id, HttpRequest request, ClaimsPrincipal user, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await AddPhotoAsync(id, PlantPhotoKind.Other, request, user, db, cancellationToken) switch
        {
            (null, var refusal) => TypedResults.StatusCode(refusal),
            _ => TypedResults.NoContent(),
        };

    /// <summary>Adds a photo, the plant's main picture if it has none yet. The plant is locked meanwhile, so two photos added at once can't both become main.</summary>
    private static async Task<(PlantPhoto? Photo, int Refusal)> AddPhotoAsync(Guid id, PlantPhotoKind kind, HttpRequest request, ClaimsPrincipal user, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(kind))
        {
            return (null, StatusCodes.Status400BadRequest);
        }

        var (image, refusal) = await ImageContent.ReadAsync(request.Body, cancellationToken);

        if (image is null)
        {
            return (null, refusal);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (!await LockPlantAsync(id, db, cancellationToken))
        {
            return (null, StatusCodes.Status404NotFound);
        }

        var count = await db.PlantPhotos.CountAsync(photo => photo.PlantId == id, cancellationToken);

        if (count >= MostPhotos)
        {
            return (null, StatusCodes.Status409Conflict);
        }

        var photo = new PlantPhoto
        {
            PlantId = id,
            Kind = kind,
            IsMain = count == 0,
            ContentType = image.ContentType,
            Content = image.Content,
            UploadedBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        };

        db.PlantPhotos.Add(photo);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (photo, 0);
    }

    private static async Task<bool> LockPlantAsync(Guid id, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM plants WHERE id = {0} FOR UPDATE", id).ToListAsync(cancellationToken) is [_];

    /// <summary>A photo's kind and caption, which anyone can change, as anyone can add one.</summary>
    private static async Task<IResult> DescribeAsync(Guid id, PlantPhotoDescription request, ClaimsPrincipal user, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var caption = string.IsNullOrWhiteSpace(request.Caption) ? null : request.Caption.Trim();

        if (!Enum.IsDefined(request.Kind) || caption?.Length > PlantPhoto.CaptionLength)
        {
            return TypedResults.BadRequest();
        }

        if (await db.PlantPhotos.FirstOrDefaultAsync(photo => photo.Id == id, cancellationToken) is not { } photo)
        {
            return TypedResults.NotFound();
        }

        photo.Kind = request.Kind;
        photo.Caption = caption;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Removed by someone else a moment ago.
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(Summary(photo, PhotoEndpoints.MayRemove(user, photo.UploadedBy)));
    }

    private static async Task<IResult> MakeMainAsync(Guid id, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (await db.PlantPhotos.AsNoTracking().Where(photo => photo.Id == id).Select(photo => (Guid?)photo.PlantId).FirstOrDefaultAsync(cancellationToken) is not { } plantId)
        {
            return TypedResults.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockPlantAsync(plantId, db, cancellationToken);

        // The old main is cleared before the new one is set, so the database never sees two.
        await db.PlantPhotos.Where(photo => photo.PlantId == plantId && photo.IsMain && photo.Id != id).ExecuteUpdateAsync(set => set.SetProperty(photo => photo.IsMain, false), cancellationToken);
        var changed = await db.PlantPhotos.Where(photo => photo.Id == id).ExecuteUpdateAsync(set => set.SetProperty(photo => photo.IsMain, true), cancellationToken);

        if (changed == 0)
        {
            return TypedResults.NotFound();
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Removes a photo. If it was the main picture, the newest of the others takes its place.</summary>
    private static async Task<IResult> RemoveAsync(Guid id, ClaimsPrincipal user, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (await db.PlantPhotos.AsNoTracking().Where(photo => photo.Id == id).Select(photo => new { photo.PlantId, photo.UploadedBy }).FirstOrDefaultAsync(cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        if (!PhotoEndpoints.MayRemove(user, found.UploadedBy))
        {
            return TypedResults.Forbid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockPlantAsync(found.PlantId, db, cancellationToken);

        if (await db.PlantPhotos.Where(photo => photo.Id == id).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            return TypedResults.NotFound();
        }

        if (!await db.PlantPhotos.AnyAsync(photo => photo.PlantId == found.PlantId && photo.IsMain, cancellationToken)
            && await db.PlantPhotos.Where(photo => photo.PlantId == found.PlantId).OrderByDescending(photo => photo.CreatedAt).ThenByDescending(photo => photo.Id).Select(photo => (Guid?)photo.Id).FirstOrDefaultAsync(cancellationToken) is { } next)
        {
            await db.PlantPhotos.Where(photo => photo.Id == next).ExecuteUpdateAsync(set => set.SetProperty(photo => photo.IsMain, true), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await db.PlantPhotos.AsNoTracking().FirstOrDefaultAsync(photo => photo.Id == id, cancellationToken) is { } photo
            ? SpeciesEndpoints.Picture(context, photo.Content, photo.ContentType, permanent: true)
            : TypedResults.NotFound();

    /// <summary>The plant's main picture, or failing that the species picture from the online plant details.</summary>
    private static async Task<IResult> GetMainAsync(Guid id, HttpContext context, HortiplannerDbContext db, SpeciesSources sources, CancellationToken cancellationToken)
    {
        if (await db.PlantPhotos.AsNoTracking()
                .Where(photo => photo.PlantId == id)
                .OrderByDescending(photo => photo.IsMain)
                .ThenByDescending(photo => photo.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken) is { } own)
        {
            return SpeciesEndpoints.Picture(context, own.Content, own.ContentType, permanent: false);
        }

        var speciesId = await db.Plants.Where(plant => plant.Id == id).Select(plant => plant.SpeciesId).FirstOrDefaultAsync(cancellationToken);

        return speciesId is { } species && await SpeciesEndpoints.FindOrFetchPhotoAsync(species, db, sources, cancellationToken) is { } photo
            ? SpeciesEndpoints.Picture(context, photo.Content, photo.ContentType, permanent: false)
            : TypedResults.NotFound();
    }

    /// <summary>Photos of the plants growing in this plant's batches, newest first, to sit beside its own photos.</summary>
    private static async Task<List<PlantBatchPhoto>> ListBatchPhotosAsync(Guid id, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await db.BatchPhotos
            .Where(photo => photo.Kind == BatchPhotoKind.Growing)
            .Join(db.Batches.Where(batch => batch.PlantId == id), photo => photo.BatchId, batch => batch.Id, (photo, batch) => new { photo, batch.TraceabilityCode })
            .OrderByDescending(row => row.photo.CreatedAt)
            .ThenByDescending(row => row.photo.Id)
            .Take(MostBatchPhotos)
            .Select(row => new PlantBatchPhoto(row.photo.Id, row.photo.BatchId, row.TraceabilityCode, row.photo.CreatedAt, row.photo.Stage, row.photo.Content.Length <= AssistantEndpoints.LargestImage))
            .ToListAsync(cancellationToken);

    private static PlantPhotoSummary Summary(PlantPhoto photo, bool canRemove) =>
        new(photo.Id, photo.Kind, photo.Caption, photo.IsMain, photo.CreatedAt == default ? DateTimeOffset.UtcNow : photo.CreatedAt, canRemove, photo.Content.Length <= AssistantEndpoints.LargestImage);

    /// <param name="CanBeAsked">Small enough to hand to Ask Hortiplanner.</param>
    internal sealed record PlantPhotoSummary(Guid Id, PlantPhotoKind Kind, string? Caption, bool IsMain, DateTimeOffset TakenAt, bool CanRemove, bool CanBeAsked);

    internal sealed record PlantPhotoDescription(PlantPhotoKind Kind, string? Caption);

    internal sealed record PlantBatchPhoto(Guid Id, Guid BatchId, string TraceabilityCode, DateTimeOffset TakenAt, Production.ProductionStage? Stage, bool CanBeAsked);
}
