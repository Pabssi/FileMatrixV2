using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using FileMatrix_Pabiran_.Data;
using FileMatrix_Pabiran_.Models;
using FileMatrix_Pabiran_.Services;
using System.Security.Cryptography;
using System.Security.Claims;

namespace FileMatrix_Pabiran_.Areas.Admin.Controllers
{
    [Area("Admin")]
    /// <summary>
    /// SettingsController: User Profile & Organization Security Hub.
    /// 
    /// RESPONSIBILITY: Manages individual user preferences (Profiles/Security) 
    /// and organization-level integration credentials (API Keys).
    /// </summary>
    public class SettingsController : BaseAdminController
    {
        private readonly UserManager<IdentityUser<int>> _userManager;
        private readonly EmailSenderService _emailSender;

        public SettingsController(
            ApplicationDbContext context,
            UserManager<IdentityUser<int>> userManager,
            EmailSenderService emailSender) : base(context)
        {
            _userManager = userManager;
            _emailSender = emailSender;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> Profile()
        {
            var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userIdStr == null) return Unauthorized();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == int.Parse(userIdStr));
            if (user == null) return NotFound();

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(User model)
        {
            var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userIdStr == null) return Unauthorized();
            var userId = int.Parse(userIdStr);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
            if (user == null) return NotFound();

            user.FirstName = model.FirstName;
            user.LastName = model.LastName;
            user.DisplayName = model.DisplayName;
            
            // Note: Email updates usually require verification, but for simplicity here we just update
            user.Email = model.Email;

            await _context.SaveChangesAsync();
            
            TempData["SuccessMessage"] = "Profile updated successfully!";
            return RedirectToAction(nameof(Profile));
        }

        /// <summary>
        /// Manages identity-sensitive settings like Passwords and Multi-Factor Authentication (MFA)
        /// by bridging to the ASP.NET Identity <see cref="UserManager"/>.
        /// </summary>
        public async Task<IActionResult> Security()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return Unauthorized();

            var identityUser = await _userManager.FindByIdAsync(userId);
            if (identityUser == null) return NotFound();

            await FmSecurityClaims.EnsureLegacyEmailClaimAsync(_userManager, identityUser);
            var claims = await _userManager.GetClaimsAsync(identityUser);

            ViewBag.HasPassword = await _userManager.HasPasswordAsync(identityUser);
            var twoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(identityUser);
            ViewBag.TwoFactorEnabled = twoFactorEnabled;

            var authenticatorKey = await _userManager.GetAuthenticatorKeyAsync(identityUser);
            ViewBag.HasAuthenticatorKey = !string.IsNullOrEmpty(authenticatorKey);

            var email2Fa = FmSecurityClaims.HasEmail2Fa(claims);
            var appMfa = FmSecurityClaims.HasAppMfa(claims) && !string.IsNullOrEmpty(authenticatorKey);
            ViewBag.Email2FaEnabled = email2Fa;
            ViewBag.AppMfaEnabled = appMfa;

            // After requesting a disable code, we land here and open the confirmation modal (no separate page).
            ViewBag.ShowDisableTwoFactorModal = string.Equals(TempData["OpenDisableTwoFactorModal"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            ViewBag.DisableTwoFactorModalError = TempData["DisableTwoFactorModalError"]?.ToString();

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string oldPassword, string newPassword, string confirmPassword)
        {
            if (newPassword != confirmPassword)
            {
                TempData["ErrorMessage"] = "New password and confirmation do not match.";
                return RedirectToAction(nameof(Security));
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return Unauthorized();

            var identityUser = await _userManager.FindByIdAsync(userId);
            if (identityUser == null) return NotFound();

            var result = await _userManager.ChangePasswordAsync(identityUser, oldPassword, newPassword);
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = "Password changed successfully!";
            }
            else
            {
                TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction(nameof(Security));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleTwoFactor(bool enabled)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return Unauthorized();

            var identityUser = await _userManager.FindByIdAsync(userId);
            if (identityUser == null) return NotFound();

            // Turning 2FA off must go through email confirmation (see RequestDisableTwoFactor).
            if (!enabled)
            {
                TempData["ErrorMessage"] = "To disable sign-in verification, open Disable and confirm the code sent to your email.";
                return RedirectToAction(nameof(Security));
            }

            var result = await _userManager.SetTwoFactorEnabledAsync(identityUser, true);
            if (result.Succeeded)
            {
                var claims = await _userManager.GetClaimsAsync(identityUser);
                if (!FmSecurityClaims.HasEmail2Fa(claims))
                {
                    await _userManager.AddClaimAsync(identityUser, new Claim(FmSecurityClaims.Email2FA, "true"));
                }
                TempData["SuccessMessage"] = "Email 2FA is on. Each sign-in will email you a code first.";
            }
            else
            {
                TempData["ErrorMessage"] = "Could not enable two-factor sign-in. Please try again.";
            }

            return RedirectToAction(nameof(Security));
        }

        /// <summary>
        /// Step 1 of disabling email 2FA: emails a short code; Security page opens a modal to enter it.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestDisableTwoFactor()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return Unauthorized();

            var identityUser = await _userManager.FindByIdAsync(userId);
            if (identityUser == null) return NotFound();

            if (!await _userManager.GetTwoFactorEnabledAsync(identityUser))
            {
                TempData["InfoMessage"] = "Two-step sign-in is already off.";
                return RedirectToAction(nameof(Security));
            }

            var email = await _userManager.GetEmailAsync(identityUser);
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Your account has no email address; we cannot send a disable code.";
                return RedirectToAction(nameof(Security));
            }

            var token = await _userManager.GenerateTwoFactorTokenAsync(identityUser, MfaConstants.EmailTokenProvider);
            var emailBody = $@"
                <div style='font-family: sans-serif; max-width: 500px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 16px; text-align: center;'>
                    <h2 style='color: #4f46e5; margin-bottom: 8px;'>Confirm disabling 2FA</h2>
                    <p style='color: #64748b; font-size: 15px;'>Enter this code in the confirmation popup on your Login &amp; Security settings page to turn off two-step sign-in.</p>
                    <div style='margin: 32px 0;'>
                        <span style='font-family: monospace; font-size: 32px; font-weight: 700; color: #1e1b4b; background: #f1f5f9; padding: 12px 24px; border-radius: 12px; letter-spacing: 4px;'>{token}</span>
                    </div>
                    <p style='font-size: 13px; color: #94a3b8;'>If you did not request this, secure your account and change your password.</p>
                </div>";

            await _emailSender.SendAsync(email, "FileMatrix — confirm turning off 2FA", emailBody);
            TempData["SuccessMessage"] = "We sent a confirmation code to your email. Enter it in the window that just opened.";
            TempData["OpenDisableTwoFactorModal"] = "true";
            return RedirectToAction(nameof(Security));
        }

        /// <summary>
        /// Legacy URL: used to be a full page; now the flow lives on Security as a modal.
        /// </summary>
        [HttpGet]
        public IActionResult ConfirmDisableTwoFactor()
        {
            TempData["InfoMessage"] = "Use Login & Security, choose Disable for email 2FA, then enter the code in the popup.";
            return RedirectToAction(nameof(Security));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmDisableTwoFactor(string code)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return Unauthorized();

            var identityUser = await _userManager.FindByIdAsync(userId);
            if (identityUser == null) return NotFound();

            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["OpenDisableTwoFactorModal"] = "true";
                TempData["DisableTwoFactorModalError"] = "Enter the code from your email.";
                return RedirectToAction(nameof(Security));
            }

            var normalized = code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var valid = await _userManager.VerifyTwoFactorTokenAsync(identityUser, MfaConstants.EmailTokenProvider, normalized);
            if (!valid)
            {
                TempData["OpenDisableTwoFactorModal"] = "true";
                TempData["DisableTwoFactorModalError"] = "That code is invalid or has expired. Use Resend code to get a new one.";
                return RedirectToAction(nameof(Security));
            }

            var allClaims = await _userManager.GetClaimsAsync(identityUser);
            foreach (var c in allClaims.Where(c => c.Type == FmSecurityClaims.Email2FA || c.Type == FmSecurityClaims.AppAuthenticator).ToList())
            {
                await _userManager.RemoveClaimAsync(identityUser, c);
            }

            await _userManager.SetTwoFactorEnabledAsync(identityUser, false);
            await _userManager.ResetAuthenticatorKeyAsync(identityUser);

            TempData["SuccessMessage"] = "Two-step sign-in has been turned off for your account.";
            return RedirectToAction(nameof(Security));
        }

        /// <summary>
        /// Creates the authenticator secret only after the user explicitly starts setup (avoids stray keys on accidental page loads).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartAuthenticatorSetup()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return Unauthorized();

            var identityUser = await _userManager.FindByIdAsync(userId);
            if (identityUser == null) return NotFound();

            await FmSecurityClaims.EnsureLegacyEmailClaimAsync(_userManager, identityUser);
            var claims = await _userManager.GetClaimsAsync(identityUser);
            if (!FmSecurityClaims.HasEmail2Fa(claims) || !await _userManager.GetTwoFactorEnabledAsync(identityUser))
            {
                TempData["ErrorMessage"] = "Turn on email 2FA first. The authenticator is an optional extra step after your email code.";
                return RedirectToAction(nameof(Security));
            }

            await _userManager.ResetAuthenticatorKeyAsync(identityUser);
            return RedirectToAction(nameof(EnableAuthenticator));
        }

        [HttpGet]
        public async Task<IActionResult> EnableAuthenticator()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return Unauthorized();

            var identityUser = await _userManager.FindByIdAsync(userId);
            if (identityUser == null) return NotFound();

            var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(identityUser);
            if (string.IsNullOrEmpty(unformattedKey))
            {
                TempData["InfoMessage"] = "Start by generating a setup key from the Security page.";
                return RedirectToAction(nameof(Security));
            }

            var claims = await _userManager.GetClaimsAsync(identityUser);
            if (FmSecurityClaims.HasAppMfa(claims))
            {
                TempData["InfoMessage"] = "Authenticator MFA is already active. Remove it first if you need to register a new device.";
                return RedirectToAction(nameof(Security));
            }

            if (!FmSecurityClaims.HasEmail2Fa(claims) || !await _userManager.GetTwoFactorEnabledAsync(identityUser))
            {
                TempData["ErrorMessage"] = "Enable email 2FA before adding the authenticator app.";
                return RedirectToAction(nameof(Security));
            }

            var email = await _userManager.GetEmailAsync(identityUser);
            var authenticatorUri = string.Format(
                "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
                System.Net.WebUtility.UrlEncode("FileMatrix"),
                System.Net.WebUtility.UrlEncode(email ?? "user"),
                unformattedKey);

            ViewBag.SharedKey = unformattedKey;
            ViewBag.AuthenticatorUri = authenticatorUri;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnableAuthenticator(string code)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return Unauthorized();

            var identityUser = await _userManager.FindByIdAsync(userId);
            if (identityUser == null) return NotFound();

            // Strip spaces and hyphens
            var verificationCode = code.Replace(" ", string.Empty).Replace("-", string.Empty);

            var is2faTokenValid = await _userManager.VerifyTwoFactorTokenAsync(
                identityUser, _userManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);

            if (!is2faTokenValid)
            {
                ModelState.AddModelError(nameof(code), "That code does not match your authenticator app. Try again.");
                // Re-populate data for the view
                var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(identityUser);
                var email = await _userManager.GetEmailAsync(identityUser);
                ViewBag.SharedKey = unformattedKey;
                ViewBag.AuthenticatorUri = string.Format(
                    "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
                    System.Net.WebUtility.UrlEncode("FileMatrix"),
                    System.Net.WebUtility.UrlEncode(email ?? "user"),
                    unformattedKey);
                return View();
            }

            var existingClaims = await _userManager.GetClaimsAsync(identityUser);
            if (!FmSecurityClaims.HasAppMfa(existingClaims))
            {
                await _userManager.AddClaimAsync(identityUser, new Claim(FmSecurityClaims.AppAuthenticator, "true"));
            }

            TempData["SuccessMessage"] = "Authenticator MFA is on. When you sign in you will enter your email code, then your app code.";
            return RedirectToAction(nameof(Security));
        }

        /// <summary>
        /// Removes authenticator binding while keeping email-based 2FA on if it was already enabled.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisableAuthenticator(string code)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return Unauthorized();

            var identityUser = await _userManager.FindByIdAsync(userId);
            if (identityUser == null) return NotFound();

            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["ErrorMessage"] = "Enter the 6-digit code from your authenticator app to remove it.";
                return RedirectToAction(nameof(Security));
            }

            var normalized = code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var ok = await _userManager.VerifyTwoFactorTokenAsync(
                identityUser,
                _userManager.Options.Tokens.AuthenticatorTokenProvider,
                normalized);

            if (!ok)
            {
                TempData["ErrorMessage"] = "Authenticator code was not valid. Nothing was changed.";
                return RedirectToAction(nameof(Security));
            }

            await _userManager.ResetAuthenticatorKeyAsync(identityUser);
            var claims = await _userManager.GetClaimsAsync(identityUser);
            foreach (var c in claims.Where(c => c.Type == FmSecurityClaims.AppAuthenticator).ToList())
            {
                await _userManager.RemoveClaimAsync(identityUser, c);
            }

            TempData["SuccessMessage"] = "Authenticator MFA removed. Sign-in will only use your email code while email 2FA stays on.";
            return RedirectToAction(nameof(Security));
        }

        /// <summary>
        /// Manages the Workplace Integration API Key. This key allows external 
        /// systems to interact with the DMS under the workplace context.
        /// </summary>
        public IActionResult Integrations()
        {
            if (CurrentWorkplace == null) return RedirectToAction("Index", "Organizations", new { area = "" });
            return View(CurrentWorkplace);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateApiKey()
        {
            if (CurrentWorkplace == null) return Unauthorized();

            // Generate a secure random API Key (Base64 encoded)
            var keyBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(keyBytes);
            }
            string newKey = "fm_" + Convert.ToBase64String(keyBytes)
                .Replace("/", "")
                .Replace("+", "")
                .Replace("=", "")
                .Substring(0, 32);

            var workplace = await _context.Workplaces.FindAsync(CurrentWorkplace.WorkplaceID);
            if (workplace != null)
            {
                workplace.IntegrationApiKey = newKey;
                await _context.SaveChangesAsync();
                
                // Update local base controller state
                CurrentWorkplace.IntegrationApiKey = newKey;

                _context.AuditLogs.Add(new AuditLog
                {
                    WorkplaceID = workplace.WorkplaceID,
                    Action = "API Key Generated",
                    EntityType = "Workplace",
                    EntityID = workplace.WorkplaceID,
                    UserID = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0"),
                    IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    PerformedAt = DateTime.UtcNow,
                    Details = "A new Integration API Key was generated."
                });
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Integrations));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevokeApiKey()
        {
            if (CurrentWorkplace == null) return Unauthorized();

            var workplace = await _context.Workplaces.FindAsync(CurrentWorkplace.WorkplaceID);
            if (workplace != null)
            {
                workplace.IntegrationApiKey = null;
                await _context.SaveChangesAsync();

                CurrentWorkplace.IntegrationApiKey = null;

                _context.AuditLogs.Add(new AuditLog
                {
                    WorkplaceID = workplace.WorkplaceID,
                    Action = "API Key Revoked",
                    EntityType = "Workplace",
                    EntityID = workplace.WorkplaceID,
                    UserID = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0"),
                    IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    PerformedAt = DateTime.UtcNow,
                    Details = "The Integration API Key was revoked."
                });
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Integrations));
        }
    }
}
