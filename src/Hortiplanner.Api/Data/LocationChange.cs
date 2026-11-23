namespace Hortiplanner.Api.Data;

/// <summary>The kind and parent that stood just before a change took effect.</summary>
internal sealed class LocationChange
{
    public long Id { get; init; }

    public required Guid LocationId { get; init; }

    public required DateTimeOffset EffectiveAt { get; init; }

    public required LocationKind PreviousKind { get; init; }

    public Guid? PreviousParentId { get; init; }
}
