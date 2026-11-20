using Hortiplanner.Api.Data;

namespace Hortiplanner.Api.Assistant;

/// <summary>What a question's tools share: the records, the clock, and what they looked at, to link from the answer.</summary>
internal sealed class ToolContext(HortiplannerDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellation)
{
    private const int MostSources = 30;

    private readonly List<AskSource> seen = [];

    public HortiplannerDbContext Db => db;

    public EventStore Store => store;

    public TimeProvider Clock => clock;

    public CancellationToken Cancellation => cancellation;

    public IReadOnlyList<AskSource> Sources => seen;

    /// <summary>Whether the owner is asking: "Needs a look" then gives the whole nursery's list, and staff their own.</summary>
    public bool IsOwner { get; init; }

    /// <summary>What the question has cost so far, in US dollars, kept as it goes so a failed answer is still counted.</summary>
    public decimal Cost { get; set; }

    /// <summary>Whose earlier conversations can be looked through: the person asking, signed in as themselves, or nobody.</summary>
    public Guid? LooksBackFor { get; init; }

    /// <summary>The kept conversation being carried on, left out when looking back.</summary>
    public Guid? ConversationId { get; init; }

    /// <summary>The parts of Hortiplanner turned off, whose tools aren't offered or run.</summary>
    public IReadOnlyCollection<NurseryFeature> FeaturesOff { get; init; } = [];

    /// <summary>Notes a record a tool returned. Kinds are "tray" (by label), "plant" and "place" (by id).</summary>
    public void Saw(string kind, string key, string label)
    {
        if (seen.Count < MostSources && !seen.Exists(item => string.Equals(item.Kind, kind, StringComparison.Ordinal) && string.Equals(item.Key, key, StringComparison.Ordinal)))
        {
            seen.Add(new AskSource(kind, key, label));
        }
    }

    /// <summary>A record an answer used, to link to.</summary>
    internal sealed record AskSource(string Kind, string Key, string Label);
}
