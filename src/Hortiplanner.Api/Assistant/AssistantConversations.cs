using System.Security.Claims;
using System.Text.Json;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Assistant;

/// <summary>
/// Each person's conversations with Ask Hortiplanner, kept for them alone. Every query is by the person's own id, so
/// anyone else, the owner included, gets "not found". Only a person signed in as themselves keeps conversations: a bench,
/// a tapped name or the nursery team account can be anyone at that device, so nothing is kept for them.
/// </summary>
internal static class AssistantConversations
{
    /// <summary>The most conversations one person keeps; a new one beyond it replaces the one least recently added to.</summary>
    internal const int MostConversations = 100;

    /// <summary>The most questions and answers in one conversation.</summary>
    internal const int MostMessages = 200;

    /// <summary>How long a conversation is kept after it was last added to.</summary>
    internal static readonly TimeSpan KeptFor = TimeSpan.FromDays(365);

    private const int AutoTitleLength = 60;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> PrivateMethods = new(StringComparer.Ordinal) { "password", "passkey", "oidc" };

    public static void MapConversationEndpoints(this RouteGroupBuilder assistant)
    {
        var conversations = assistant.MapGroup("/conversations");
        conversations.MapGet("/", ListAsync);
        conversations.MapGet("/{id:guid}", GetAsync).Produces<ConversationDetails>();
        conversations.MapPut("/{id:guid}/title", RenameAsync);
        conversations.MapDelete("/{id:guid}", DeleteAsync);
        conversations.MapDelete("/", DeleteAllAsync);
    }

    /// <summary>Signed in as themselves, by their own password, passkey or staff login, and not at a bench.</summary>
    internal static bool IsPrivate(ClaimsPrincipal user) =>
        user.FindFirst(AccountServices.BenchSessionClaim) is null
        && user.FindFirstValue(AccountServices.SignInMethodClaim) is { } method
        && PrivateMethods.Contains(method);

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// This person's own conversations, and nobody else's, while they're signed in as themselves. One not added to for a
    /// year has gone, whether or not it has been swept away yet.
    /// </summary>
    private static IQueryable<AssistantConversation> Mine(HortiplannerDbContext db, ClaimsPrincipal user, TimeProvider clock) =>
        IsPrivate(user) ? Of(db, UserId(user), clock) : db.AssistantConversations.Where(_ => false);

    /// <summary>One person's conversations, less any a year old.</summary>
    internal static IQueryable<AssistantConversation> Of(HortiplannerDbContext db, Guid ownerId, TimeProvider clock)
    {
        var since = clock.GetUtcNow() - KeptFor;
        return db.AssistantConversations.Where(conversation => conversation.OwnerId == ownerId && conversation.UpdatedAt >= since);
    }

    private static async Task<List<ConversationSummary>> ListAsync(ClaimsPrincipal user, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken) =>
        await Mine(db, user, clock)
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .Select(conversation => new ConversationSummary(conversation.Id, conversation.Title, conversation.UpdatedAt, conversation.AboutKind, conversation.AboutLabel))
            .ToListAsync(cancellationToken);

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await Mine(db, user, clock).AsNoTracking().FirstOrDefaultAsync(conversation => conversation.Id == id, cancellationToken) is not { } conversation)
        {
            return TypedResults.NotFound();
        }

        var messages = await db.AssistantMessages.AsNoTracking()
            .Where(message => message.ConversationId == id)
            .OrderBy(message => message.CreatedAt).ThenBy(message => message.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new ConversationDetails(
            conversation.Id,
            conversation.Title,
            conversation.UpdatedAt,
            conversation.AboutKind,
            conversation.AboutLabel,
            messages.ConvertAll(message => new SavedMessage(
                message.Id,
                message.Role,
                message.Text,
                message.PhotoCount,
                message.Sources is null ? [] : JsonSerializer.Deserialize<List<ToolContext.AskSource>>(message.Sources, Json) ?? [],
                message.Cost,
                message.CreatedAt))));
    }

    private static async Task<IResult> RenameAsync(Guid id, ConversationTitle request, ClaimsPrincipal user, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();

        if (string.IsNullOrEmpty(title) || title.Length > AssistantConversation.TitleLength)
        {
            return TypedResults.BadRequest();
        }

        return await Mine(db, user, clock).Where(conversation => conversation.Id == id).ExecuteUpdateAsync(set => set.SetProperty(conversation => conversation.Title, title), cancellationToken) == 0
            ? TypedResults.NotFound()
            : TypedResults.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal user, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken) =>
        await Mine(db, user, clock).Where(conversation => conversation.Id == id).ExecuteDeleteAsync(cancellationToken) == 0
            ? TypedResults.NotFound()
            : TypedResults.NoContent();

    private static async Task<IResult> DeleteAllAsync(ClaimsPrincipal user, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!IsPrivate(user))
        {
            return TypedResults.NotFound();
        }

        await Mine(db, user, clock).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// The conversation so far, oldest first, as the model is given it; null if it isn't this person's. A question sent
    /// with photos says so, as the photos themselves aren't kept.
    /// </summary>
    internal static async Task<List<AssistantEndpoints.AskMessage>?> HistoryAsync(HortiplannerDbContext db, Guid ownerId, Guid id, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!await Of(db, ownerId, clock).AnyAsync(conversation => conversation.Id == id, cancellationToken))
        {
            return null;
        }

        return await db.AssistantMessages.AsNoTracking()
            .Where(message => message.ConversationId == id)
            .OrderBy(message => message.CreatedAt).ThenBy(message => message.Id)
            .Select(message => new AssistantEndpoints.AskMessage(
                message.Role,
                message.PhotoCount == 0 ? message.Text
                : message.PhotoCount == 1 ? message.Text + "\n(The person sent a photo with this, which isn't kept.)"
                : message.Text + "\n(The person sent " + message.PhotoCount + " photos with this, which aren't kept.)"))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Keeps a question and its answer together, in a new conversation or the one carried on. A new one beyond the most
    /// a person keeps replaces the one they least recently added to, and conversations a year old go, everyone's.
    /// </summary>
    internal static async Task<(Guid Id, string Title)> SaveAsync(HortiplannerDbContext db, Guid ownerId, Guid? id, AssistantEndpoints.AskAbout? about, string question, int photos, AssistantEndpoints.AskAnswer answer, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AssistantConversation conversation;

        if (id is { } existing && await Of(db, ownerId, clock).FirstOrDefaultAsync(item => item.Id == existing, cancellationToken) is { } found)
        {
            conversation = found;
            conversation.UpdatedAt = now;
        }
        else
        {
            await db.AssistantConversations.Where(item => item.UpdatedAt < now - KeptFor).ExecuteDeleteAsync(cancellationToken);
            var kept = db.AssistantConversations.Where(item => item.OwnerId == ownerId).OrderByDescending(item => item.UpdatedAt).Skip(MostConversations - 1).Select(item => item.Id);
            await db.AssistantConversations.Where(item => kept.Contains(item.Id)).ExecuteDeleteAsync(cancellationToken);
            var (kind, label) = await AboutAsync(db, about, cancellationToken);
            conversation = new AssistantConversation { OwnerId = ownerId, Title = TitleOf(question), CreatedAt = now, UpdatedAt = now, AboutKind = kind, AboutLabel = label };
            db.AssistantConversations.Add(conversation);
        }

        db.AssistantMessages.Add(new AssistantMessage { ConversationId = conversation.Id, Role = "user", Text = question, PhotoCount = photos, CreatedAt = now });
        db.AssistantMessages.Add(new AssistantMessage
        {
            ConversationId = conversation.Id,
            Role = "assistant",
            Text = Cut(answer.Answer, AssistantMessage.LongestText),
            Sources = JsonSerializer.Serialize(answer.Sources, Json),
            Cost = answer.Cost,

            // A moment after its question, so the two always read in order.
            CreatedAt = now.AddTicks(10),
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (conversation.Id, conversation.Title);
    }

    internal static async Task<int> CountMessagesAsync(HortiplannerDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.AssistantMessages.CountAsync(message => message.ConversationId == id, cancellationToken);

    /// <summary>The first question, on one line and cut at a word, so the list reads at a glance.</summary>
    internal static string TitleOf(string question)
    {
        var line = string.Join(' ', question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (line.Length <= AutoTitleLength)
        {
            return line.Length == 0 ? "A question" : line;
        }

        var cut = Cut(line, AutoTitleLength);
        var space = cut.LastIndexOf(' ');
        return $"{(space > AutoTitleLength / 2 ? cut[..space] : cut).TrimEnd(',', '.', ';', ':')}…";
    }

    /// <summary>What a conversation was started about, worked out from the records rather than taken from the screen.</summary>
    private static async Task<(string? Kind, string? Label)> AboutAsync(HortiplannerDbContext db, AssistantEndpoints.AskAbout? about, CancellationToken cancellationToken)
    {
        if (about is null)
        {
            return (null, null);
        }

        var plant = about.PlantId is { } plantId
            ? await db.Plants.Where(item => item.Id == plantId).Select(item => new { item.CommonName, item.Variety }).FirstOrDefaultAsync(cancellationToken)
            : null;
        var plantName = plant is null ? null : plant.Variety is null ? plant.CommonName : $"{plant.CommonName} '{plant.Variety}'";
        var place = about.PlaceId is { } placeId
            ? await db.Locations.Where(item => item.Id == placeId).Select(item => item.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var tray = about.TrayLabel?.ToUpperInvariant();

        var (kind, label) = (about.PhotoId ?? about.PlantPhotoId ?? about.PlacePhotoId) is not null
            ? ("photo", $"a photo of {plantName ?? place ?? (tray is null ? "a plant" : $"tray {tray}")}")
            : tray is not null ? ("tray", $"tray {tray}")
            : place is not null ? ("place", place)
            : plantName is not null ? ("plant", plantName)
            : ((string?)null, (string?)null);

        return (kind, label is null ? null : Cut(label, AssistantConversation.AboutLabelLength));
    }

    /// <summary>Text cut to a length, never through the middle of a character made of two, such as an emoji.</summary>
    internal static string Cut(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        var cut = text[..length];
        return char.IsHighSurrogate(cut[^1]) ? cut[..^1] : cut;
    }

    internal sealed record ConversationSummary(Guid Id, string Title, DateTimeOffset UpdatedAt, string? AboutKind, string? AboutLabel);

    internal sealed record ConversationDetails(Guid Id, string Title, DateTimeOffset UpdatedAt, string? AboutKind, string? AboutLabel, IReadOnlyList<SavedMessage> Messages);

    internal sealed record SavedMessage(Guid Id, string Role, string Text, int PhotoCount, IReadOnlyList<ToolContext.AskSource> Sources, decimal? Cost, DateTimeOffset At);

    internal sealed record ConversationTitle(string? Title);
}
