namespace Hortiplanner.Api.Data;

/// <summary>One of the nursery's own photos of a plant. The main one is shown in place of any species picture.</summary>
internal sealed class PlantPhoto
{
    /// <summary>The most a caption may say, so it fits under a photo on a phone.</summary>
    public const int CaptionLength = 200;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid PlantId { get; init; }

    public PlantPhotoKind Kind { get; set; } = PlantPhotoKind.Other;

    /// <summary>What the photo shows, in the words of whoever added it. Plain text.</summary>
    public string? Caption { get; set; }

    /// <summary>The plant's main picture, shown on lists and its page. A plant with photos has exactly one.</summary>
    public bool IsMain { get; set; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }

    /// <summary>Who added it. Null for the single picture each plant had before photos were kept in numbers.</summary>
    public Guid? UploadedBy { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}
