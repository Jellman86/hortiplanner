namespace Hortiplanner.Api.Data;

/// <summary>A question or an answer in a saved conversation. Photos sent with a question aren't kept, only how many there were.</summary>
internal sealed class AssistantMessage
{
    /// <summary>Longest answer kept; a question is never longer than 4,000 characters.</summary>
    public const int LongestText = 12_000;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid ConversationId { get; init; }

    /// <summary>"user" or "assistant".</summary>
    public required string Role { get; init; }

    public required string Text { get; init; }

    public int PhotoCount { get; init; }

    /// <summary>For an answer, the records it looked at, as JSON.</summary>
    public string? Sources { get; init; }

    /// <summary>For an answer, what it cost in US dollars.</summary>
    public decimal? Cost { get; init; }

    /// <summary>When it was said: a conversation reads in this order.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
