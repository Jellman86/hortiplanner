namespace Hortiplanner.Api.Data;

/// <summary>What a photo of a plant shows.</summary>
internal enum PlantPhotoKind
{
    Flower,
    Foliage,

    /// <summary>The whole plant: its shape and size.</summary>
    Habit,

    Label,
    Other,
}
