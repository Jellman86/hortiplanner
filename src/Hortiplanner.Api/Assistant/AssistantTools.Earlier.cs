using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Assistant;

/// <summary>Looking back through the asking person's own earlier conversations, and nobody else's.</summary>
internal static partial class AssistantTools
{
    private const int MostEarlierMatches = 8;
    private const int MostSearchWords = 5;
    private const int SnippetLength = 300;

    /// <summary>
    /// Questions and answers from this person's other conversations that mention the words, most words first, then
    /// newest, each cut to the part around the first word found.
    /// </summary>
    private static async Task<object> EarlierAsync(string query, ToolContext context)
    {
        if (context.LooksBackFor is not { } person)
        {
            return new { problem = "Earlier conversations aren't kept here." };
        }

        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim('.', ',', '?', '!', '"', '\'').ToLowerInvariant())
            .Where(word => word.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .Take(MostSearchWords)
            .ToList();

        if (words.Count == 0)
        {
            return new { problem = "Give a word or two to look for." };
        }

        var patterns = words.ConvertAll(word => "%" + word.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal) + "%");
        var mine = AssistantConversations.Of(context.Db, person, context.Clock).Where(conversation => conversation.Id != context.ConversationId);
        var found = await context.Db.AssistantMessages.AsNoTracking()
            .Where(message => mine.Any(conversation => conversation.Id == message.ConversationId)
                && patterns.Any(pattern => EF.Functions.ILike(message.Text, pattern)))
            .OrderByDescending(message => message.CreatedAt)
            .Take(MostRows)
            .Select(message => new
            {
                message.Text,
                message.Role,
                message.CreatedAt,
                Title = mine.Where(conversation => conversation.Id == message.ConversationId).Select(conversation => conversation.Title).First(),
            })
            .ToListAsync(context.Cancellation);

        if (found.Count > 0)
        {
            context.Saw("earlier", "mine", "Your earlier conversations");
        }

        return found
            .OrderByDescending(item => words.Count(word => item.Text.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Take(MostEarlierMatches)
            .Select(item => new
            {
                conversation = item.Title,
                when = item.CreatedAt.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-GB")),
                said = string.Equals(item.Role, "user", StringComparison.Ordinal) ? "the person" : "you",
                text = Snippet(item.Text, words),
            });
    }

    private static string Snippet(string text, List<string> words)
    {
        if (text.Length <= SnippetLength)
        {
            return text;
        }

        var at = words.Select(word => text.IndexOf(word, StringComparison.OrdinalIgnoreCase)).Where(index => index >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Clamp(at - (SnippetLength / 3), 0, text.Length - SnippetLength);

        // Never through the middle of a character made of two, such as an emoji.
        if (start > 0 && char.IsLowSurrogate(text[start]))
        {
            start--;
        }

        return $"{(start > 0 ? "…" : string.Empty)}{AssistantConversations.Cut(text[start..], SnippetLength)}{(start + SnippetLength < text.Length ? "…" : string.Empty)}";
    }
}
