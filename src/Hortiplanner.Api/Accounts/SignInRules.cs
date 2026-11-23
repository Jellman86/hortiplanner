using System.Globalization;
using System.Security.Claims;
using Hortiplanner.Api.Data;

namespace Hortiplanner.Api.Accounts;

/// <summary>What the nursery's chosen sign-in level allows.</summary>
internal static class SignInRules
{
    public const string TeamName = "Nursery team";

    private const int CredentialCheckSeconds = 10 * 60;

    public static bool IsPersonalMethod(string? method) => method is "password" or "passkey" or "oidc";

    public static bool IsPersonal(ClaimsPrincipal user) =>
        user.FindFirst(AccountServices.BenchSessionClaim) is null
        && IsPersonalMethod(user.FindFirstValue(AccountServices.SignInMethodClaim));

    public static bool IsRecentCredentialCheck(string? checkedAt, TimeProvider clock)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        return long.TryParse(checkedAt, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && seconds <= now && seconds >= now - CredentialCheckSeconds;
    }

    /// <summary>Whether someone can sign in just by tapping their name. Never the owner.</summary>
    public static bool CanTapName(SignInLevel level, bool chosenForThem, bool isOwner) =>
        !isOwner && level switch
        {
            SignInLevel.Open or SignInLevel.Names => true,
            SignInLevel.Personal => chosenForThem,
            _ => false,
        };

    /// <summary>Whether a person can be set up to sign in by tapping their name.</summary>
    public static bool AllowsNameOnly(SignInLevel level) => level != SignInLevel.Strict;

    public static bool BenchNeedsPin(SignInLevel level) => level is SignInLevel.Personal or SignInLevel.Strict;
}
