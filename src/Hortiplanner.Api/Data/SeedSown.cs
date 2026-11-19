namespace Hortiplanner.Api.Data;

/// <summary>How much seed went into a tray nobody has counted since, so a screen can say what was sown instead of nothing.</summary>
internal sealed record SeedSown(SownMeasure SownAs, decimal? SownAmount, PacketShare? PacketShare, string? PacketSays)
{
    /// <summary>The seed a tray started with, for a tray not counted since it was sown with only a measure; otherwise null.</summary>
    public static SeedSown? For(bool notCounted, SownMeasure? sownAs, decimal? sownAmount, PacketShare? packetShare, string? packetSays) =>
        notCounted && sownAs is { } measure and not SownMeasure.Counted
            ? new SeedSown(measure, sownAmount, packetShare, packetSays)
            : null;
}
