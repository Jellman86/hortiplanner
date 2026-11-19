namespace Hortiplanner.Api.Data;

/// <summary>
/// One person's conversation with Ask Hortiplanner, kept for them alone to come back to. Nobody else can open it in
/// Hortiplanner, the owner included. It goes when they delete it, or a year after they last added to it.
/// </summary>
internal sealed class AssistantConversation
{
    public const int TitleLength = 80;

    public const int AboutLabelLength = 200;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid OwnerId { get; init; }

    public required string Title { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>What it was started about, if anything: "tray", "plant", "place" or "photo".</summary>
    public string? AboutKind { get; init; }

    /// <summary>What it was about, in words worked out from the records, such as "tray C000012" or a plant's name.</summary>
    public string? AboutLabel { get; init; }
}
