using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hortiplanner.Api.Assistant;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;
using static Hortiplanner.Tests.AssistantTests;

namespace Hortiplanner.Tests;

/// <summary>Saved conversations: kept for the person who asked and for nobody else, the owner included.</summary>
public sealed class ConversationTests(PostgresFixture database)
{
    private static async Task<JsonElement> AskAsync(HttpClient client, string question, Guid? conversationId = null, object? about = null)
    {
        using var asked = await client.PostAsJsonAsync(Route("/api/assistant/ask"), new { messages = new[] { new { role = "user", text = question } }, conversationId, about }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        return Json(await asked.Content.ReadAsStringAsync(Cancellation));
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<JsonElement>(Route("/api/assistant/conversations"), Cancellation)).EnumerateArray()];

    private static async Task<HttpStatusCode> StatusOfAsync(Task<HttpResponseMessage> sending)
    {
        using var response = await sending;
        return response.StatusCode;
    }

    /// <summary>A kept conversation of so many lines, put straight into the database.</summary>
    internal static async Task<Guid> KeepAsync(TwoPersonNursery nursery, Guid ownerId, int lines, Func<int, string> text, DateTimeOffset? at = null)
    {
        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();
        var when = at ?? DateTimeOffset.UtcNow;
        var conversation = new AssistantConversation { OwnerId = ownerId, Title = "Kept", CreatedAt = when, UpdatedAt = when };
        db.AssistantConversations.Add(conversation);
        for (var index = 0; index < lines; index++)
        {
            db.AssistantMessages.Add(new AssistantMessage { ConversationId = conversation.Id, Role = index % 2 == 0 ? "user" : "assistant", Text = text(index), CreatedAt = when.AddTicks(index * 10) });
        }

        await db.SaveChangesAsync(Cancellation);
        return conversation.Id;
    }

    [Fact]
    public async Task A_conversation_is_kept_and_carried_on_from_what_was_kept_not_the_screen()
    {
        var openRouter = new PretendOpenRouter();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, outsideWorld: openRouter);
        await SetUpAsync(nursery, openRouter);
        Assert.True((await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/assistant/status"), Cancellation)).GetProperty("savesConversations").GetBoolean());

        var first = await AskAsync(nursery.Owner, "  What's   growing in the greenhouses at the moment, and is any of it ready to sell yet?  ");
        var id = first.GetProperty("conversationId").GetGuid();
        Assert.Equal("What's growing in the greenhouses at the moment, and is any…", first.GetProperty("title").GetString());

        // What the screen claims was said before is ignored: the kept conversation is what goes.
        openRouter.Questions.Clear();
        using (var carried = await nursery.Owner.PostAsJsonAsync(Route("/api/assistant/ask"), new { messages = new[] { new { role = "user", text = "Something made up" }, new { role = "user", text = "And the polytunnel?" } }, conversationId = id }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.OK, carried.StatusCode);
            Assert.Equal(id, Json(await carried.Content.ReadAsStringAsync(Cancellation)).GetProperty("conversationId").GetGuid());
        }

        Assert.Contains("is any of it ready to sell", openRouter.Questions[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Something made up", openRouter.Questions[0].Body, StringComparison.Ordinal);

        var kept = await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/assistant/conversations/{id}"), Cancellation);
        Assert.Equal(["user", "assistant", "user", "assistant"], kept.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("role").GetString()), StringComparer.Ordinal);
        Assert.Equal("You have 40 Lavender growing.", kept.GetProperty("messages")[1].GetProperty("text").GetString());
        Assert.Single(await ListAsync(nursery.Owner));

        Assert.Equal(HttpStatusCode.NoContent, await StatusOfAsync(nursery.Owner.PutAsJsonAsync(Route($"/api/assistant/conversations/{id}/title"), new { title = "  Greenhouses  " }, Cancellation)));
        Assert.Equal("Greenhouses", (await ListAsync(nursery.Owner))[0].GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, await StatusOfAsync(nursery.Owner.PutAsJsonAsync(Route($"/api/assistant/conversations/{id}/title"), new { title = " " }, Cancellation)));
    }

    [Fact]
    public async Task Nobody_else_can_open_continue_rename_or_delete_a_conversation_the_owner_included()
    {
        var openRouter = new PretendOpenRouter();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, outsideWorld: openRouter);
        await SetUpAsync(nursery, openRouter, forStaff: true);
        var staffs = (await AskAsync(nursery.Staff, "A private worry about the vine weevils")).GetProperty("conversationId").GetGuid();

        Assert.Empty(await ListAsync(nursery.Owner));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Owner, $"/api/assistant/conversations/{staffs}"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(nursery.Owner.PutAsJsonAsync(Route($"/api/assistant/conversations/{staffs}/title"), new { title = "Mine now" }, Cancellation)));
        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(nursery.Owner.DeleteAsync(Route($"/api/assistant/conversations/{staffs}"), Cancellation)));
        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(nursery.Owner.PostAsJsonAsync(Route("/api/assistant/ask"), new { messages = new[] { new { role = "user", text = "Carry on" } }, conversationId = staffs }, Cancellation)));
        Assert.Equal(HttpStatusCode.NoContent, await StatusOfAsync(nursery.Owner.DeleteAsync(Route("/api/assistant/conversations"), Cancellation)));

        // Still the staff member's, untouched, and never in what the owner sees about spending.
        var theirs = Assert.Single(await ListAsync(nursery.Staff));
        Assert.Equal("A private worry about the vine weevils", theirs.GetProperty("title").GetString());
        Assert.DoesNotContain("vine weevils", await nursery.Owner.GetStringAsync(Route("/api/assistant/settings"), Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Someone_signed_in_by_tapping_their_name_keeps_nothing_and_cant_look_back()
    {
        var openRouter = new PretendOpenRouter();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, outsideWorld: openRouter);
        await SetUpAsync(nursery, openRouter, forStaff: true);
        await AllowNameOnlyAsync(nursery.Owner);
        var jo = Json(await PostForTextAsync(nursery.Owner, "/api/staff", new { name = "Jo Reed", nameOnly = true })).GetProperty("id").GetGuid();
        using var tapped = Client(nursery.App);
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(tapped, "/api/account/sign-in/name", new { userId = jo }));

        Assert.False((await tapped.GetFromJsonAsync<JsonElement>(Route("/api/assistant/status"), Cancellation)).GetProperty("savesConversations").GetBoolean());
        var answer = await AskAsync(tapped, "What's growing?");
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("conversationId").ValueKind);
        Assert.Empty(await ListAsync(tapped));
        Assert.DoesNotContain("my_earlier_conversations", openRouter.Questions[0].Body, StringComparison.Ordinal);

        await using var scope = nursery.App.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>().AssistantConversations.CountAsync(Cancellation));
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("Names")]
    public async Task Tapping_a_staff_members_name_cannot_add_credentials_or_open_their_conversations(string level)
    {
        var openRouter = new PretendOpenRouter();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, outsideWorld: openRouter);
        await SetUpAsync(nursery, openRouter, forStaff: true);
        await SetSignInLevelAsync(nursery.Owner, level);
        const string question = "A private question about the tulips";
        var saved = (await AskAsync(nursery.Staff, question)).GetProperty("conversationId").GetGuid();
        var options = await PostForTextAsync(nursery.Staff, "/api/account/settings/passkey-options");
        using var newKey = new TestAuthenticator();

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Staff, "/api/account/sign-in/name", new { userId = nursery.StaffId }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Staff, "/api/account/settings/password", new { newPassword = "a long new password" }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Staff, "/api/account/settings/passkey-options"));
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Staff, "/api/account/settings/passkey", new { credential = Json(newKey.Register(options)) }));
        Assert.Empty(await ListAsync(nursery.Staff));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Staff, $"/api/assistant/conversations/{saved}"));

        openRouter.Questions.Clear();
        var answer = await AskAsync(nursery.Staff, "What did I ask?", saved);
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("conversationId").ValueKind);
        Assert.NotEmpty(openRouter.Questions);
        Assert.All(openRouter.Questions, sent => Assert.DoesNotContain(question, sent.Body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_full_conversation_is_refused_and_the_oldest_and_year_old_ones_make_way()
    {
        var openRouter = new PretendOpenRouter();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, outsideWorld: openRouter);
        await SetUpAsync(nursery, openRouter);
        var owner = await nursery.OwnerIdAsync();

        var full = await KeepAsync(nursery, owner, AssistantConversations.MostMessages - 1, index => $"Line {index}");
        using (var refused = await nursery.Owner.PostAsJsonAsync(Route("/api/assistant/ask"), new { messages = new[] { new { role = "user", text = "One more" } }, conversationId = full }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("ConversationFull", Json(await refused.Content.ReadAsStringAsync(Cancellation)).GetProperty("problem").GetString());
        }

        var yearOld = await KeepAsync(nursery, owner, 2, index => $"Old {index}", DateTimeOffset.UtcNow.AddDays(-400));
        var oldest = await KeepAsync(nursery, owner, 2, index => $"Oldest {index}", DateTimeOffset.UtcNow.AddDays(-300));
        for (var count = 3; count <= AssistantConversations.MostConversations; count++)
        {
            await KeepAsync(nursery, owner, 2, index => $"Filler {index}", DateTimeOffset.UtcNow.AddDays(-count));
        }

        await AskAsync(nursery.Owner, "A new question");
        var listed = (await ListAsync(nursery.Owner)).ConvertAll(conversation => conversation.GetProperty("id").GetGuid());
        Assert.Equal(AssistantConversations.MostConversations, listed.Count);
        Assert.DoesNotContain(yearOld, listed);
        Assert.DoesNotContain(oldest, listed);
        Assert.Contains(full, listed);
    }

    [Fact]
    public async Task Deleting_a_conversation_or_all_of_them_leaves_what_questions_cost()
    {
        var openRouter = new PretendOpenRouter();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, outsideWorld: openRouter);
        await SetUpAsync(nursery, openRouter);
        var first = (await AskAsync(nursery.Owner, "First")).GetProperty("conversationId").GetGuid();
        await AskAsync(nursery.Owner, "Second");

        Assert.Equal(HttpStatusCode.NoContent, await StatusOfAsync(nursery.Owner.DeleteAsync(Route($"/api/assistant/conversations/{first}"), Cancellation)));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Owner, $"/api/assistant/conversations/{first}"));
        Assert.Single(await ListAsync(nursery.Owner));
        Assert.Equal(HttpStatusCode.NoContent, await StatusOfAsync(nursery.Owner.DeleteAsync(Route("/api/assistant/conversations"), Cancellation)));
        Assert.Empty(await ListAsync(nursery.Owner));

        var spent = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/assistant/status"), Cancellation)).GetProperty("spent");
        Assert.True(spent.GetProperty("inAll").GetDecimal() > 0);
    }

    [Fact]
    public async Task Looking_back_finds_only_the_persons_own_earlier_words()
    {
        var openRouter = new PretendOpenRouter();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, outsideWorld: openRouter);
        await SetUpAsync(nursery, openRouter, forStaff: true);
        var mine = (await AskAsync(nursery.Owner, "The vine weevil grubs in the heuchera pots")).GetProperty("conversationId").GetGuid();
        await AskAsync(nursery.Staff, "Staff note about vine weevil and the cyclamen");
        var now = (await AskAsync(nursery.Owner, "Something else")).GetProperty("conversationId").GetGuid();
        var offered = JsonNode.Parse(openRouter.Questions[^1].Body)!["tools"]!.AsArray().Select(tool => tool!["function"]!["name"]!.GetValue<string>()).ToList();
        Assert.True(offered.Contains("my_earlier_conversations"), string.Join(",", offered));

        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();
        var context = new ToolContext(db, scope.ServiceProvider.GetRequiredService<EventStore>(), TimeProvider.System, Cancellation) { LooksBackFor = await nursery.OwnerIdAsync(), ConversationId = now };
        var found = await AssistantTools.RunAsync("my_earlier_conversations", new JsonObject { ["query"] = "vine weevil" }, context);

        Assert.Contains("heuchera", found, StringComparison.Ordinal);
        Assert.DoesNotContain("cyclamen", found, StringComparison.Ordinal);
        Assert.Contains(context.Sources, source => string.Equals(source.Kind, "earlier", StringComparison.Ordinal));
        Assert.NotEqual(Guid.Empty, mine);

        // Without someone to look back for, the tool refuses.
        var refused = await AssistantTools.RunAsync("my_earlier_conversations", new JsonObject { ["query"] = "vine weevil" }, new ToolContext(db, scope.ServiceProvider.GetRequiredService<EventStore>(), TimeProvider.System, Cancellation));
        Assert.Contains("available here", refused, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("password", false, true)]
    [InlineData("passkey", false, true)]
    [InlineData("oidc", false, true)]
    [InlineData("name", false, false)]
    [InlineData("team", false, false)]
    [InlineData("pin", true, false)]
    [InlineData("passkey", true, false)]
    [InlineData(null, false, false)]
    public void Only_someone_signed_in_as_themselves_and_not_at_a_bench_keeps_conversations(string? method, bool bench, bool keeps)
    {
        var claims = new List<System.Security.Claims.Claim> { new(System.Security.Claims.ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()) };
        if (method is not null)
        {
            claims.Add(new(Hortiplanner.Api.Accounts.AccountServices.SignInMethodClaim, method));
        }

        if (bench)
        {
            claims.Add(new(Hortiplanner.Api.Accounts.AccountServices.BenchSessionClaim, "true"));
        }

        Assert.Equal(keeps, AssistantConversations.IsPrivate(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test"))));
    }

    [Fact]
    public async Task A_conversation_a_year_old_has_gone_even_before_it_is_swept_away_and_titles_are_checked()
    {
        var openRouter = new PretendOpenRouter();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, outsideWorld: openRouter);
        await SetUpAsync(nursery, openRouter);
        var old = await KeepAsync(nursery, await nursery.OwnerIdAsync(), 2, index => $"Long ago {index}", DateTimeOffset.UtcNow.AddDays(-366));

        Assert.Empty(await ListAsync(nursery.Owner));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Owner, $"/api/assistant/conversations/{old}"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(nursery.Owner.PostAsJsonAsync(Route("/api/assistant/ask"), new { messages = new[] { new { role = "user", text = "Carry on" } }, conversationId = old }, Cancellation)));

        var id = (await AskAsync(nursery.Owner, "Fresh question")).GetProperty("conversationId").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, await StatusOfAsync(nursery.Owner.PutAsJsonAsync(Route($"/api/assistant/conversations/{id}/title"), new { title = new string('a', AssistantConversation.TitleLength + 1) }, Cancellation)));
        Assert.Equal("a…", AssistantConversations.Cut("a\U0001F33F", 2) + "…");
    }

    [Fact]
    public async Task Clearing_the_records_clears_everyones_conversations_and_keeps_what_they_cost()
    {
        var openRouter = new PretendOpenRouter();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, demo: true, outsideWorld: openRouter);
        await SetUpAsync(nursery, openRouter, forStaff: true);
        await AskAsync(nursery.Owner, "Owner's question");
        await AskAsync(nursery.Staff, "Staff question");

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, "/api/settings/clear-records", new { confirmation = "clear everything" }));

        Assert.Empty(await ListAsync(nursery.Owner));
        Assert.Empty(await ListAsync(nursery.Staff));
        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();
        Assert.Equal(0, await db.AssistantMessages.CountAsync(Cancellation));
        Assert.True(await db.AssistantSpends.CountAsync(Cancellation) >= 2);
    }
}
