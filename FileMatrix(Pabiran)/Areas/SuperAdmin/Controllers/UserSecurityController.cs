using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FileMatrix_Pabiran_.Data;
using FileMatrix_Pabiran_.Models;
using FileMatrix_Pabiran_.Services;

namespace FileMatrix_Pabiran_.Areas.SuperAdmin.Controllers
{
    /// <summary>
    /// SuperAdmin-only: manage another account’s password, email 2FA, and authenticator MFA (mirrors workplace Admin → Settings → Security).
    /// </summary>
    [Area("SuperAdmin")]
    [Route("SuperAdmin/Users")]
    [Authorize(Roles = "SuperAdmin")]
    public class UserSecurityController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser<int>> _userManager;
        private readonly EmailSenderService _emailSender;

        public UserSecurityController(
            ApplicationDbContext context,
            UserManager<IdentityUser<int>> userManager,
            EmailSenderService emailSender)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
        }

        private async Task<IdentityUser<int>?> FindIdentityUserAsync(int id) =>
            await _userManager.FindByIdAsync(id.ToString());

        private async Task LogSuperAdminUserAuditAsync(int targetUserId, string action, string details)
        {
            var actorIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int? actorId = int.TryParse(actorIdStr, out var a) ? a : null;
            _context.AuditLogs.Add(new AuditLog
            {
                WorkplaceID = null,
                Action = action,
                EntityType = "User",
                EntityID = targetUserId,
                UserID = actorId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                PerformedAt = DateTime.UtcNow,
                Details = details
            });
            await _context.SaveChangesAsync();
        }

        [HttpGet("{id:int}/Security")]
        public async Task<IActionResult> Security(int id)
        {
            var identityUser = await FindIdentityUserAsync(id);
            if (identityUser == null) return NotFound();

            var profile = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserID == id);
            ViewBag.TargetUserId = id;
            ViewBag.TargetEmail = profile?.Email ?? await _userManager.GetEmailAsync(identityUser);
            ViewBag.TargetDisplayName = profile?.DisplayName ?? profile?.Username ?? identityUser.UserName;

            await FmSecurityClaims.EnsureLegacyEmailClaimAsync(_userManager, identityUser);
            var claims = await _userManager.GetClaimsAsync(identityUser);

            ViewBag.HasPassword = await _userManager.HasPasswordAsync(identityUser);
            ViewBag.TwoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(identityUser);

            var authenticatorKey = await _userManager.GetAuthenticatorKeyAsync(identityUser);
            ViewBag.HasAuthenticatorKey = !string.IsNullOrEmpty(authenticatorKey);

            var email2Fa = FmSecurityClaims.HasEmail2Fa(claims);
            var appMfa = FmSecurityClaims.HasAppMfa(claims) && !string.IsNullOrEmpty(authenticatorKey);
            ViewBag.Email2FaEnabled = email2Fa;
            ViewBag.AppMfaEnabled = appMfa;

            ViewBag.ShowDisableTwoFactorModal = string.Equals(TempData["OpenDisableTwoFactorModal"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            ViewBag.DisableTwoFactorModalError = TempData["DisableTwoFactorModalError"]?.ToString();

            return View();
        }

        /// <summary>
        /// Sets a new password on the user’s Identity account (no current password). Clears lockout after success so they can sign in.
        /// </summary>
        [HttpPost("{id:int}/Security/SetPassword")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPassword(int id, string newPassword, string confirmPassword)
        {
            if (newPassword != confirmPassword)
            {
                TempData["ErrorMessage"] = "New password and confirmation do not match.";
                return RedirectToAction(nameof(Security), new { id });
            }

            var identityUser = await FindIdentityUserAsync(id);
            if (identityUser == null) return NotFound();

            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(identityUser);
            var result = await _userManager.ResetPasswordAsync(identityUser, resetToken, newPassword ?? string.Empty);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Security), new { id });
            }

            await _userManager.SetLockoutEndDateAsync(identityUser, null);
            await _userManager.ResetAccessFailedCountAsync(identityUser);

            TempData["SuccessMessage"] = "Password updated for this account. Lockout was cleared so they can sign in.";
            await LogSuperAdminUserAuditAsync(id, "SuperAdmin: Password set", $"SuperAdmin set a new password for user ID {id}.");
            return RedirectToAction(nameof(Security), new { id });
        }

        [HttpPost("{id:int}/Security/ToggleTwoFactor")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleTwoFactor(int id, bool enabled)
        {
            var identityUser = await FindIdentityUserAsync(id);
            if (identityUser == null) return NotFound();

            if (!enabled)
            {
                TempData["ErrorMessage"] = "To disable sign-in verification, use Disable — we email a code to this user’s address.";
                return RedirectToAction(nameof(Security), new { id });
            }

            var result = await _userManager.SetTwoFactorEnabledAsync(identityUser, true);
            if (result.Succeeded)
            {
                var claims = await _userManager.GetClaimsAsync(identityUser);
                if (!FmSecurityClaims.HasEmail2Fa(claims))
                    await _userManager.AddClaimAsync(identityUser, new Claim(FmSecurityClaims.Email2FA, "true"));
                TempData["SuccessMessage"] = "Email 2FA is on for this account.";
                await LogSuperAdminUserAuditAsync(id, "SuperAdmin: Email 2FA enabled", $"SuperAdmin enabled email 2FA for user ID {id}.");
            }
            else
                TempData["ErrorMessage"] = "Could not enable two-factor sign-in for this account.";

            return RedirectToAction(nameof(Security), new { id });
        }

        [HttpPost("{id:int}/Security/RequestDisableTwoFactor")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestDisableTwoFactor(int id)
        {
            var identityUser = await FindIdentityUserAsync(id);
            if (identityUser == null) return NotFound();

            if (!await _userManager.GetTwoFactorEnabledAsync(identityUser))
            {
                TempData["InfoMessage"] = "Two-step sign-in is already off for this account.";
                return RedirectToAction(nameof(Security), new { id });
            }

            var email = await _userManager.GetEmailAsync(identityUser);
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "This account has no email; we cannot send a disable code.";
                return RedirectToAction(nameof(Security), new { id });
            }

            var token = await _userManager.GenerateTwoFactorTokenAsync(identityUser, MfaConstants.EmailTokenProvider);
            var emailBody = $@"
                <div style='font-family: sans-serif; max-width: 500px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 16px; text-align: center;'>
                    <h2 style='color: #4f46e5; margin-bottom: 8px;'>Confirm disabling 2FA</h2>
                    <p style='color: #64748b; font-size: 15px;'>A platform administrator started turning off extra sign-in for your FileMatrix account. Enter this code in the confirmation window they have open.</p>
                    <div style='margin: 32px 0;'>
                        <span style='font-family: monospace; font-size: 32px; font-weight: 700; color: #1e1b4b; background: #f1f5f9; padding: 12px 24px; border-radius: 12px; letter-spacing: 4px;'>{token}</span>
                    </div>
                    <p style='font-size: 13px; color: #94a3b8;'>If you did not expect this, secure your account and change your password.</p>
                </div>";

            await _emailSender.SendAsync(email, "FileMatrix — confirm turning off 2FA", emailBody);
            TempData["SuccessMessage"] = "We emailed a confirmation code to this user’s address. Enter it in the popup.";
            TempData["OpenDisableTwoFactorModal"] = "true";
            await LogSuperAdminUserAuditAsync(id, "SuperAdmin: 2FA disable requested", $"SuperAdmin requested email code to disable 2FA for user ID {id}.");
            return RedirectToAction(nameof(Security), new { id });
        }

        [HttpPost("{id:int}/Security/ConfirmDisableTwoFactor")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmDisableTwoFactor(int id, string code)
        {
            var identityUser = await FindIdentityUserAsync(id);
            if (identityUser == null) return NotFound();

            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["OpenDisableTwoFactorModal"] = "true";
                TempData["DisableTwoFactorModalError"] = "Enter the code from the user’s email.";
                return RedirectToAction(nameof(Security), new { id });
            }

            var normalized = code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var valid = await _userManager.VerifyTwoFactorTokenAsync(identityUser, MfaConstants.EmailTokenProvider, normalized);
            if (!valid)
            {
                TempData["OpenDisableTwoFactorModal"] = "true";
                TempData["DisableTwoFactorModalError"] = "That code is invalid or has expired. Use Resend code to get a new one.";
                return RedirectToAction(nameof(Security), new { id });
            }

            var allClaims = await _userManager.GetClaimsAsync(identityUser);
            foreach (var c in allClaims.Where(c => c.Type == FmSecurityClaims.Email2FA || c.Type == FmSecurityClaims.AppAuthenticator).ToList())
                await _userManager.RemoveClaimAsync(identityUser, c);

            await _userManager.SetTwoFactorEnabledAsync(identityUser, false);
            await _userManager.ResetAuthenticatorKeyAsync(identityUser);

            TempData["SuccessMessage"] = "Two-step sign-in has been turned off for this account.";
            await LogSuperAdminUserAuditAsync(id, "SuperAdmin: 2FA disabled", $"SuperAdmin disabled email 2FA and authenticator for user ID {id}.");
            return RedirectToAction(nameof(Security), new { id });
        }

        [HttpPost("{id:int}/Security/StartAuthenticatorSetup")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartAuthenticatorSetup(int id)
        {
            var identityUser = await FindIdentityUserAsync(id);
            if (identityUser == null) return NotFound();

            await FmSecurityClaims.EnsureLegacyEmailClaimAsync(_userManager, identityUser);
            var claims = await _userManager.GetClaimsAsync(identityUser);
            if (!FmSecurityClaims.HasEmail2Fa(claims) || !await _userManager.GetTwoFactorEnabledAsync(identityUser))
            {
                TempData["ErrorMessage"] = "Turn on email 2FA for this account first.";
                return RedirectToAction(nameof(Security), new { id });
            }

            await _userManager.ResetAuthenticatorKeyAsync(identityUser);
            return RedirectToAction(nameof(EnableAuthenticator), new { id });
        }

        [HttpGet("{id:int}/Security/Authenticator")]
        public async Task<IActionResult> EnableAuthenticator(int id)
        {
            var identityUser = await FindIdentityUserAsync(id);
            if (identityUser == null) return NotFound();

            var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(identityUser);
            if (string.IsNullOrEmpty(unformattedKey))
            {
                TempData["InfoMessage"] = "Generate a setup key from the Security page first.";
                return RedirectToAction(nameof(Security), new { id });
            }

            var claims = await _userManager.GetClaimsAsync(identityUser);
            if (FmSecurityClaims.HasAppMfa(claims))
            {
                TempData["InfoMessage"] = "Authenticator MFA is already active for this account.";
                return RedirectToAction(nameof(Security), new { id });
            }

            if (!FmSecurityClaims.HasEmail2Fa(claims) || !await _userManager.GetTwoFactorEnabledAsync(identityUser))
            {
                TempData["ErrorMessage"] = "Enable email 2FA before adding the authenticator app.";
                return RedirectToAction(nameof(Security), new { id });
            }

            var email = await _userManager.GetEmailAsync(identityUser);
            var authenticatorUri = string.Format(
                "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
                System.Net.WebUtility.UrlEncode("FileMatrix"),
                System.Net.WebUtility.UrlEncode(email ?? "user"),
                unformattedKey);

            ViewBag.SharedKey = unformattedKey;
            ViewBag.AuthenticatorUri = authenticatorUri;
            ViewBag.TargetUserId = id;
            return View();
        }

        [HttpPost("{id:int}/Security/Authenticator")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnableAuthenticator(int id, string code)
        {
            var identityUser = await FindIdentityUserAsync(id);
            if (identityUser == null) return NotFound();

            var verificationCode = code.Replace(" ", string.Empty).Replace("-", string.Empty);

            var is2faTokenValid = await _userManager.VerifyTwoFactorTokenAsync(
                identityUser, _userManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);

            if (!is2faTokenValid)
            {
                ModelState.AddModelError(nameof(code), "That code does not match the authenticator app.");
                var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(identityUser);
                var email = await _userManager.GetEmailAsync(identityUser);
                ViewBag.SharedKey = unformattedKey;
                ViewBag.AuthenticatorUri = string.Format(
                    "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
                    System.Net.WebUtility.UrlEncode("FileMatrix"),
                    System.Net.WebUtility.UrlEncode(email ?? "user"),
                    unformattedKey);
                ViewBag.TargetUserId = id;
                return View();
            }

            var existingClaims = await _userManager.GetClaimsAsync(identityUser);
            if (!FmSecurityClaims.HasAppMfa(existingClaims))
                await _userManager.AddClaimAsync(identityUser, new Claim(FmSecurityClaims.AppAuthenticator, "true"));

            TempData["SuccessMessage"] = "Authenticator MFA is on for this account.";
            await LogSuperAdminUserAuditAsync(id, "SuperAdmin: Authenticator MFA enabled", $"SuperAdmin completed authenticator setup for user ID {id}.");
            return RedirectToAction(nameof(Security), new { id });
        }

        [HttpPost("{id:int}/Security/DisableAuthenticator")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisableAuthenticator(int id, string code)
        {
            var identityUser = await FindIdentityUserAsync(id);
            if (identityUser == null) return NotFound();

            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["ErrorMessage"] = "Enter the 6-digit app code to remove authenticator MFA.";
                return RedirectToAction(nameof(Security), new { id });
            }

            var normalized = code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var ok = await _userManager.VerifyTwoFactorTokenAsync(
                identityUser,
                _userManager.Options.Tokens.AuthenticatorTokenProvider,
                normalized);

            if (!ok)
            {
                TempData["ErrorMessage"] = "Authenticator code was not valid. Nothing was changed.";
                return RedirectToAction(nameof(Security), new { id });
            }

            await _userManager.ResetAuthenticatorKeyAsync(identityUser);
            var claims = await _userManager.GetClaimsAsync(identityUser);
            foreach (var c in claims.Where(c => c.Type == FmSecurityClaims.AppAuthenticator).ToList())
                await _userManager.RemoveClaimAsync(identityUser, c);

            TempData["SuccessMessage"] = "Authenticator MFA removed for this account (email 2FA unchanged if it was on).";
            await LogSuperAdminUserAuditAsync(id, "SuperAdmin: Authenticator MFA removed", $"SuperAdmin removed authenticator for user ID {id}.");
            return RedirectToAction(nameof(Security), new { id });
        }
    }
}
