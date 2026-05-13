using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace FileMatrix_Pabiran_.Models
{
    /// <summary>
    /// Separates email 2FA from optional authenticator MFA using Identity user claims (no extra DB tables).
    /// Email 2FA = sign-in email code; App MFA = second step after email when both are enabled.
    /// </summary>
    public static class FmSecurityClaims
    {
        public const string Email2FA = "FileMatrix.Security.Email2FA";
        public const string AppAuthenticator = "FileMatrix.Security.AppAuthenticator";

        public static bool HasEmail2Fa(IEnumerable<Claim> claims) =>
            claims.Any(c => c.Type == Email2FA && string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase));

        public static bool HasAppMfa(IEnumerable<Claim> claims) =>
            claims.Any(c => c.Type == AppAuthenticator && string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Older accounts used TwoFactorEnabled without claims — treat them as email-2FA-only.
        /// </summary>
        public static async Task EnsureLegacyEmailClaimAsync(UserManager<IdentityUser<int>> userManager, IdentityUser<int> user)
        {
            var claims = await userManager.GetClaimsAsync(user);
            if (HasEmail2Fa(claims) || HasAppMfa(claims)) return;
            if (!await userManager.GetTwoFactorEnabledAsync(user)) return;
            await userManager.AddClaimAsync(user, new Claim(Email2FA, "true"));
        }
    }
}
