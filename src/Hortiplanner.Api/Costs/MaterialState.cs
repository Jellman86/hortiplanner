namespace Hortiplanner.Api.Costs;

/// <summary>Where a batch's seed or plant cost comes from, or why there is none yet.</summary>
internal enum MaterialState
{
    NoAnswer,
    Amount,
    Free,
    NotKnown,
    Cuttings,
    Purchase,
}
