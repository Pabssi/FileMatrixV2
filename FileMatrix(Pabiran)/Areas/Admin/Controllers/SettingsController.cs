using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using FileMatrix_Pabiran_.Data;
using FileMatrix_Pabiran_.Models;
using System.Security.Cryptography;

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

        public SettingsController(ApplicationDbContext context, UserManager<IdentityUser<int>> userManager) : base(context)
        {
            _userManager = userManager;
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
        /// Manages identity-sensitive settings like Passwords and Two-Factor Authentication 
        /// by bridging to the ASP.NET Identity <see cref="UserManager"/>.
        /// </summary>
        public async Task<IActionResult> Security()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return Unauthorized();

            var identityUser = await _userManager.FindByIdAsync(userId);
            if (identityUser == null) return NotFound();

            ViewBag.HasPassword = await _userManager.HasPasswordAsync(identityUser);
            ViewBag.TwoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(identityUser);

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

            var result = await _userManager.SetTwoFactorEnabledAsync(identityUser, enabled);
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = $"Two-Factor Authentication has been {(enabled ? "enabled" : "disabled")}.";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to update Two-Factor Authentication status.";
            }

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
                    PerformedAt = DateTime.UtcNow,
                    Details = "The Integration API Key was revoked."
                });
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Integrations));
        }
    }
}
