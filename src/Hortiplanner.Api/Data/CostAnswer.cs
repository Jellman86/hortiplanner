namespace Hortiplanner.Api.Data;

internal enum CostAnswer
{
    Amount,

    /// <summary>It cost nothing, such as the nursery's own seed or a gift.</summary>
    Free,

    /// <summary>The owner has said nobody knows what it cost.</summary>
    NotKnown,

    /// <summary>Only ever as a replacement, for a cost added by mistake.</summary>
    Removed,
}
