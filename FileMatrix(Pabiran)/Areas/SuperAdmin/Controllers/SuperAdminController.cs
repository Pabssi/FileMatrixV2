using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FileMatrix_Pabiran_.Data;
using FileMatrix_Pabiran_.Models;
using FileMatrix_Pabiran_.Areas.SuperAdmin.Models;
using FileMatrix_Pabiran_.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace FileMatrix_Pabiran_.Areas.SuperAdmin.Controllers
{
    /// <summary>
    /// SuperAdmin Controller - Platform Management & Infrastructure Oversight.
    /// 
    /// SECURITY STANDARD: The "Privacy Shield".
    /// SuperAdmins have platform-wide visibility for infrastructure management but are 
    /// EXPLICITLY BLOCKED from accessing individual document content, comments, or 
    /// private descriptions to ensure tenant data privacy.
    /// </summary>
    [Area("SuperAdmin")]
    [Route("SuperAdmin")]
    [Authorize(Roles = "SuperAdmin")]
    public class SuperAdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser<int>> _userManager;
        private readonly EmailSenderService _emailSender;

        private const int SuperAdminAuditPageSize = 25;

        public SuperAdminController(
            ApplicationDbContext context,
            UserManager<IdentityUser<int>> userManager,
            EmailSenderService emailSender)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
        }

        [HttpGet("Documents")]
        public async Task<IActionResult> Documents()
        {
            // Privacy Standard: Return only infrastructure-level metadata.
            // Simple Query: Get a list of every document in the system along with which organization it belongs to.
            var documents = await _context.Documents
                .Select(d => new {
                    d.DocumentID,
                    d.Title, // Necessary for identification in management
                    WorkplaceName = _context.Workplaces.Where(w => w.WorkplaceID == d.WorkplaceID).Select(w => w.Name).FirstOrDefault(),
                    d.CreatedAt,
                    VersionCount = _context.DocumentVersions.Count(dv => dv.DocumentID == d.DocumentID),
                    Size = _context.DocumentVersions.Where(dv => dv.DocumentID == d.DocumentID).OrderByDescending(dv => dv.VersionNumber).Select(dv => dv.FileSizeBytes).FirstOrDefault(),
                    MimeType = _context.DocumentVersions.Where(dv => dv.DocumentID == d.DocumentID).OrderByDescending(dv => dv.VersionNumber).Select(dv => dv.MimeType).FirstOrDefault()
                })
                .ToListAsync();

            ViewBag.Documents = documents;
            return View();
        }

        [HttpGet("Documents/Metadata/{id}")]
        public async Task<IActionResult> GetDocumentMetadata(int id)
        {
            // Privacy Standard: Explicitly return ONLY non-sensitive infrastructure metadata.
            // Description, Comments, and File Paths are strictly excluded.
            // Simple Query: Find the specific details of one document by its ID.
            var metadata = await _context.Documents
                .Where(d => d.DocumentID == id)
                .Select(d => new {
                    d.DocumentID,
                    d.Title,
                    Organization = _context.Workplaces.Where(w => w.WorkplaceID == d.WorkplaceID).Select(w => w.Name).FirstOrDefault(),
                    d.CreatedAt,
                    d.UpdatedAt,
                    d.Status,
                    LatestVersion = _context.DocumentVersions.Where(dv => dv.DocumentID == d.DocumentID).OrderByDescending(dv => dv.VersionNumber).Select(dv => new {
                        dv.VersionNumber,
                        dv.FileSizeBytes,
                        dv.MimeType,
                        dv.UploadedAt
                    }).FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            if (metadata == null) return NotFound();

            return Json(new { success = true, data = metadata });
        }

        [HttpGet("Settings")]
        public async Task<IActionResult> Settings()
        {
            var settings = await _context.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
            return View(settings);
        }

        [HttpPost("Settings/Update")]
        public async Task<IActionResult> UpdateSettings(Dictionary<string, string> settings)
        {
            foreach (var setting in settings)
            {
                // ASP.NET Core checkbox behavior often sends "true,false" or "false" 
                // We normalize this to just 'true' or 'false'
                var value = setting.Value;
                if (value.Contains(","))
                {
                    value = value.Split(',').First();
                }

                // Query 3: Check if this specific setting already exists in our master list
                var dbSetting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == setting.Key);
                if (dbSetting != null)
                {
                    dbSetting.Value = value;
                    dbSetting.LastUpdated = DateTime.UtcNow;
                }
                else
                {
                    // Upsert: Create if it doesn't exist
                    _context.SystemSettings.Add(new SystemSetting
                    {
                        Key = setting.Key,
                        Value = value,
                        LastUpdated = DateTime.UtcNow,
                        Description = "Auto-generated from UI"
                    });
                }
            }
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Platform settings updated successfully.";
            return RedirectToAction(nameof(Settings));
        }

        [HttpGet("Organizations")]
        public async Task<IActionResult> Organizations()
        {
            var organizations = await _context.Workplaces
                .Select(w => new {
                    w.WorkplaceID,
                    w.Name,
                    w.CreatedAt,
                    UserCount = _context.WorkplaceMembers.Count(wu => wu.WorkplaceID == w.WorkplaceID),
                    DocumentCount = _context.Documents.Count(d => d.WorkplaceID == w.WorkplaceID),
                    w.IsActive
                })
                .ToListAsync();

            ViewBag.Organizations = organizations;
            return View();
        }

        [HttpPost("Organizations/ToggleStatus/{id}")]
        public async Task<IActionResult> ToggleWorkplaceStatus(int id)
        {
            // Simple Query: Find the specific organization we want to activate or deactivate.
            var workplace = await _context.Workplaces.FindAsync(id);
            if (workplace != null)
            {
                workplace.IsActive = !workplace.IsActive;
                // Simple Query: Save the new 'Active' or 'Inactive' status to the database.
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Organizations));
        }

        [HttpGet("Organizations/Provision")]
        public IActionResult ProvisionWorkplace()
        {
            return View();
        }

        [HttpPost("Organizations/Provision")]
        public async Task<IActionResult> ProvisionWorkplace(Workplace model)
        {
            if (ModelState.IsValid)
            {
                model.CreatedAt = DateTime.UtcNow;
                model.IsActive = true;
                _context.Workplaces.Add(model);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Organizations));
            }
            return View(model);
        }

        [HttpGet("Users")]
        public async Task<IActionResult> Users()
        {
            var userRoles = await _context.UserRoles.ToListAsync();
            var roles = await _context.Roles.ToListAsync();
            
            var users = await _userManager.Users
                .Select(u => new {
                    u.Id,
                    u.Email,
                    u.UserName,
                    Role = _context.Roles.Where(r => r.Id == _context.UserRoles.Where(ur => ur.UserId == u.Id).Select(ur => ur.RoleId).FirstOrDefault()).Select(r => r.Name).FirstOrDefault() ?? "User",
                    IsActive = _context.Users.Where(du => du.UserID == u.Id).Select(du => du.IsActive).FirstOrDefault()
                })
                .ToListAsync();

            ViewBag.Users = users;
            return View();
        }

        [HttpGet("Users/CreateSuperAdmin")]
        public IActionResult CreateSuperAdmin()
        {
            return View(new CreateSuperAdminViewModel());
        }

        /// <summary>
        /// Creates an Identity + DMS user with the SuperAdmin role and emails them a password-set link.
        /// </summary>
        [HttpPost("Users/CreateSuperAdmin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSuperAdmin(CreateSuperAdminViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var normalizedInvite = model.Email.Trim().ToLowerInvariant();
            if (!await TryValidateAndConsumeSuperAdminPinAsync(model.ActionConfirmationPin, SuperAdminActionPinPurposes.CreateSuperAdmin, null, normalizedInvite))
            {
                ModelState.AddModelError(nameof(model.ActionConfirmationPin), "Invalid or expired verification code. Request a new code from the confirmation dialog.");
                return View(model);
            }

            var email = model.Email.Trim();
            var existing = await _userManager.FindByEmailAsync(email);
            if (existing != null)
            {
                ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
                return View(model);
            }

            var display = string.IsNullOrWhiteSpace(model.DisplayName) ? email.Split('@')[0] : model.DisplayName!.Trim();

            var identityUser = new IdentityUser<int>
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            // No password yet — invitee sets it via the reset link (avoids Identity policy mismatches on a throwaway password).
            var createResult = await _userManager.CreateAsync(identityUser);
            if (!createResult.Succeeded)
            {
                foreach (var err in createResult.Errors)
                    ModelState.AddModelError(string.Empty, err.Description);
                return View(model);
            }

            var roleResult = await _userManager.AddToRoleAsync(identityUser, "SuperAdmin");
            if (!roleResult.Succeeded)
            {
                await _userManager.DeleteAsync(identityUser);
                ModelState.AddModelError(string.Empty, "Could not assign the SuperAdmin role. Try again.");
                return View(model);
            }

            try
            {
                if (!await _context.Users.AnyAsync(u => u.UserID == identityUser.Id))
                {
                    _context.Users.Add(new User
                    {
                        UserID = identityUser.Id,
                        Username = email,
                        Email = email,
                        PasswordHash = identityUser.PasswordHash ?? "",
                        DisplayName = display,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        Role = 0
                    });
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                await _userManager.RemoveFromRoleAsync(identityUser, "SuperAdmin");
                await _userManager.DeleteAsync(identityUser);
                ModelState.AddModelError(string.Empty, "Could not create the profile record: " + ex.Message);
                return View(model);
            }

            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(identityUser);
            var resetUrl = Url.Action(
                "ResetPassword",
                "Account",
                new { token = resetToken, email = email },
                protocol: Request.Scheme);

            if (string.IsNullOrWhiteSpace(resetUrl))
            {
                await TryRollbackInvitedSuperAdminAsync(identityUser.Id);
                ModelState.AddModelError(string.Empty, "Could not build the password setup link. Check the site URL / HTTPS configuration.");
                return View(model);
            }

            // Do not HtmlEncode the whole URL — it would break http(s):// and query separators.
            var hrefForEmail = resetUrl.Replace("'", "%27", StringComparison.Ordinal);

            var inviteBody = $@"
                <div style='font-family:sans-serif;max-width:560px;margin:0 auto;padding:28px;border:1px solid #e2e8f0;border-radius:16px;'>
                    <h2 style='color:#4f46e5;margin:0 0 12px;'>You have been invited as a FileMatrix Super Administrator</h2>
                    <p style='color:#475569;font-size:15px;line-height:1.6;'>Hello {(System.Net.WebUtility.HtmlEncode(display))},</p>
                    <p style='color:#475569;font-size:15px;line-height:1.6;'>A platform administrator created your Super Admin account for <strong>{System.Net.WebUtility.HtmlEncode(email)}</strong>.</p>
                    <p style='color:#475569;font-size:15px;line-height:1.6;'>Use the button below to <strong>choose your own password</strong> before signing in. This link expires in a few hours.</p>
                    <div style='margin:28px 0;text-align:center;'>
                        <a href='{hrefForEmail}' style='background:#4f46e5;color:#fff;padding:14px 32px;border-radius:999px;text-decoration:none;font-weight:700;display:inline-block;'>
                            Set your password
                        </a>
                    </div>
                    <p style='font-size:13px;color:#94a3b8;line-height:1.5;'>If you were not expecting this, ignore this email. The account can be deactivated by your organization.</p>
                </div>";

            try
            {
                await _emailSender.SendAsync(email, "FileMatrix: Super Admin invitation", inviteBody);
            }
            catch (Exception ex)
            {
                await TryRollbackInvitedSuperAdminAsync(identityUser.Id);
                var detail = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
                ModelState.AddModelError(string.Empty, "Could not send the invitation email (account was not created). " + detail);
                return View(model);
            }

            var actorIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int? actorId = int.TryParse(actorIdStr, out var aid) ? aid : null;
            _context.AuditLogs.Add(new AuditLog
            {
                WorkplaceID = null,
                Action = "SuperAdmin invited",
                EntityType = "User",
                EntityID = identityUser.Id,
                UserID = actorId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                PerformedAt = DateTime.UtcNow,
                Details = $"Invited new SuperAdmin {email}."
            });
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Super Admin invite sent to {email}. They must set a password from the email before signing in.";
            return RedirectToAction(nameof(Users));
        }

        /// <summary>
        /// Removes DMS row and Identity user if an invite failed after the account was created.
        /// </summary>
        private async Task TryRollbackInvitedSuperAdminAsync(int identityUserId)
        {
            try
            {
                var dmsRow = await _context.Users.FindAsync(identityUserId);
                if (dmsRow != null)
                {
                    _context.Users.Remove(dmsRow);
                    await _context.SaveChangesAsync();
                }
            }
            catch { /* best-effort cleanup */ }

            try
            {
                var u = await _userManager.FindByIdAsync(identityUserId.ToString());
                if (u != null)
                    await _userManager.DeleteAsync(u);
            }
            catch { /* best-effort cleanup */ }
        }

        /// <summary>
        /// Sends a short-lived PIN to the signed-in Super Admin's email so sensitive actions can be confirmed in the UI.
        /// </summary>
        [HttpPost("Users/SendActionConfirmationPin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendActionConfirmationPin([FromForm] string purpose, [FromForm] int? targetUserId, [FromForm] string? inviteEmail)
        {
            if (purpose != SuperAdminActionPinPurposes.CreateSuperAdmin && purpose != SuperAdminActionPinPurposes.ToggleUserStatus)
                return Json(new { success = false, message = "Unsupported action." });

            if (purpose == SuperAdminActionPinPurposes.ToggleUserStatus && (!targetUserId.HasValue || targetUserId.Value <= 0))
                return Json(new { success = false, message = "Invalid user for this action." });

            string? normalizedInvite = null;
            if (purpose == SuperAdminActionPinPurposes.CreateSuperAdmin)
            {
                if (string.IsNullOrWhiteSpace(inviteEmail))
                    return Json(new { success = false, message = "Enter the invitee email before sending a code." });
                var ea = new EmailAddressAttribute();
                if (!ea.IsValid(inviteEmail.Trim()))
                    return Json(new { success = false, message = "Enter a valid invite email before sending a code." });
                normalizedInvite = inviteEmail.Trim().ToLowerInvariant();
            }

            if (IsSuperAdminPinSendThrottled())
                return Json(new { success = false, message = "Please wait a few seconds before requesting another code." });

            var actor = await _userManager.GetUserAsync(User);
            var toEmail = actor?.Email;
            if (string.IsNullOrWhiteSpace(toEmail))
                return Json(new { success = false, message = "Your account has no email on file; verification codes cannot be sent." });

            var code = GenerateSixDigitPin();
            var pending = new PendingSuperAdminPin
            {
                Code = code,
                Purpose = purpose,
                TargetUserId = purpose == SuperAdminActionPinPurposes.ToggleUserStatus ? targetUserId : null,
                NormalizedInviteEmail = normalizedInvite,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                FailedAttempts = 0
            };

            HttpContext.Session.SetString(SessionPendingPinKey, JsonSerializer.Serialize(pending));
            MarkSuperAdminPinSent();

            var actionLabel = purpose == SuperAdminActionPinPurposes.CreateSuperAdmin
                ? "invite a new Super Administrator"
                : "change a user's active / suspended status";

            var pinBody = $@"
                <div style='font-family:sans-serif;max-width:520px;margin:0 auto;padding:24px;border:1px solid #e2e8f0;border-radius:12px;'>
                    <h2 style='color:#0f172a;margin:0 0 12px;font-size:18px;'>Super Admin verification code</h2>
                    <p style='color:#475569;font-size:15px;line-height:1.6;margin:0 0 16px;'>Use this code in the FileMatrix Super Admin portal to confirm you want to <strong>{System.Net.WebUtility.HtmlEncode(actionLabel)}</strong>.</p>
                    <p style='font-size:28px;letter-spacing:0.25em;font-weight:700;color:#111827;margin:0 0 16px;'>{System.Net.WebUtility.HtmlEncode(code)}</p>
                    <p style='font-size:13px;color:#94a3b8;line-height:1.5;margin:0;'>This code expires in 10 minutes. If you did not start this action, change your password and review audit logs.</p>
                </div>";

            try
            {
                await _emailSender.SendAsync(toEmail, "FileMatrix Super Admin: your verification code", pinBody);
            }
            catch (Exception ex)
            {
                HttpContext.Session.Remove(SessionPendingPinKey);
                var detail = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
                return Json(new { success = false, message = "Could not send email: " + detail });
            }

            return Json(new { success = true, message = $"A code was sent to {MaskEmailForDisplay(toEmail)}." });
        }

        [HttpPost("Users/ToggleStatus/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUserStatus(int id, [FromForm] string? actionConfirmationPin)
        {
            if (!await TryValidateAndConsumeSuperAdminPinAsync(actionConfirmationPin, SuperAdminActionPinPurposes.ToggleUserStatus, id, null))
            {
                TempData["ErrorMessage"] = "Verification code missing or invalid. Open the confirmation dialog, send a code to your email, then try again.";
                return RedirectToAction(nameof(Users));
            }

            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                user.IsActive = !user.IsActive;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Users));
        }

        [HttpGet("AuditLogs")]
        public async Task<IActionResult> AuditLogs(string severity = "all", int page = 1)
        {
            severity = NormalizeAuditSeverity(severity);
            if (page < 1) page = 1;

            var baseQuery = _context.AuditLogs.AsNoTracking();
            var filtered = ApplyPlatformAuditSeverity(baseQuery, severity);
            var totalCount = await filtered.CountAsync();
            var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)SuperAdminAuditPageSize);
            if (page > totalPages) page = totalPages;

            var logs = await filtered
                .OrderByDescending(l => l.PerformedAt)
                .Skip((page - 1) * SuperAdminAuditPageSize)
                .Take(SuperAdminAuditPageSize)
                .ToListAsync();

            ViewBag.Logs = logs;
            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.Severity = severity;
            ViewBag.PagerAction = nameof(AuditLogs);
            ViewBag.PageSize = SuperAdminAuditPageSize;
            return View();
        }

        [HttpGet("LoginAttempts")]
        public async Task<IActionResult> LoginAttempts(string severity = "all", int page = 1)
        {
            severity = NormalizeAuditSeverity(severity);
            if (page < 1) page = 1;

            var securityBase = _context.AuditLogs.AsNoTracking().Where(l => l.EntityType == "Security");
            var today = DateTime.UtcNow.Date;

            ViewBag.TodayCount = await securityBase.CountAsync(l => l.PerformedAt >= today);
            ViewBag.UniqueIPs = await securityBase
                .Where(l => l.IpAddress != null && l.IpAddress != "")
                .Select(l => l.IpAddress)
                .Distinct()
                .CountAsync();
            ViewBag.LockedCount = await securityBase.CountAsync(l =>
                l.Action == "Login Locked" || l.Action == "Account Locked");
            ViewBag.SecurityTotalAll = await securityBase.CountAsync();

            var filtered = ApplyLoginAttemptsSeverity(securityBase, severity);
            var totalCount = await filtered.CountAsync();
            var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)SuperAdminAuditPageSize);
            if (page > totalPages) page = totalPages;

            var logs = await filtered
                .OrderByDescending(l => l.PerformedAt)
                .Skip((page - 1) * SuperAdminAuditPageSize)
                .Take(SuperAdminAuditPageSize)
                .ToListAsync();

            ViewBag.Logs = logs;
            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.Severity = severity;
            ViewBag.PagerAction = nameof(LoginAttempts);
            ViewBag.PageSize = SuperAdminAuditPageSize;
            return View();
        }

        [HttpGet("Backfills")]
        public async Task<IActionResult> Backfills()
        {
            var tasks = await _context.SystemInfrastructureTasks.ToListAsync();
            
            // Fallback: If for any reason the tasks weren't seeded (e.g. startup error), seed them now
            if (!tasks.Any())
            {
                await DbInitializer.InitializeAsync(HttpContext.RequestServices);
                tasks = await _context.SystemInfrastructureTasks.ToListAsync();
            }

            ViewBag.Tasks = tasks;
            return View();
        }

        [HttpPost("Backfills/Run")]
        public async Task<IActionResult> RunTask(string key)
        {
            var task = await _context.SystemInfrastructureTasks.FirstOrDefaultAsync(t => t.Key == key);
            if (task == null) return NotFound();

            task.Status = "Running";
            task.LastRun = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Production logic: This should be async/background.
            // For now, we execute it directly and update status to show completion.
            string result = "Task executed successfully.";
            bool success = true;

            try
            {
                switch (key)
                {
                    case "normalize-usernames":
                        var usersToNormalize = await _context.Users.ToListAsync();
                        int normalizedCount = 0;
                        foreach (var u in usersToNormalize)
                        {
                            var old = u.Username;
                            u.Username = u.Username?.Trim().ToLowerInvariant();
                            if (old != u.Username) normalizedCount++;
                        }
                        await _context.SaveChangesAsync();
                        result = $"Normalized {normalizedCount} usernames.";
                        break;

                    case "sync-storage":
                        var docs = await _context.Documents.ToListAsync();
                        int syncedCount = 0;
                        foreach (var d in docs)
                        {
                            var latestVersion = await _context.DocumentVersions
                                .Where(dv => dv.DocumentID == d.DocumentID)
                                .OrderByDescending(dv => dv.VersionNumber)
                                .FirstOrDefaultAsync();
                            
                            if (latestVersion != null && d.CurrentVersionID != latestVersion.VersionID)
                            {
                                d.CurrentVersionID = latestVersion.VersionID;
                                syncedCount++;
                            }
                        }
                        await _context.SaveChangesAsync();
                        result = $"Synchronized metadata for {syncedCount} documents.";
                        break;

                    case "role-consistency":
                        var roleIds = await _context.Roles.ToDictionaryAsync(r => r.Name, r => r.Id);
                        var userRoles = await _context.UserRoles.ToListAsync();
                        var dmsUsers = await _context.Users.ToListAsync();
                        int consistencyCount = 0;

                        foreach (var du in dmsUsers)
                        {
                            // Sync Roles (simplified check)
                            if (!userRoles.Any(ur => ur.UserId == du.UserID))
                            {
                                // Attach default 'User' role if missing
                                if (roleIds.ContainsKey("User"))
                                {
                                    _context.UserRoles.Add(new IdentityUserRole<int> 
                                    { 
                                        UserId = du.UserID, 
                                        RoleId = roleIds["User"] 
                                    });
                                    consistencyCount++;
                                }
                            }
                        }
                        await _context.SaveChangesAsync();
                        result = $"Validated {dmsUsers.Count} users. Applied fixes to {consistencyCount}.";
                        break;
                }
            }
            catch (Exception ex)
            {
                success = false;
                result = $"Error: {ex.Message}";
            }

            task.Status = success ? "Healthy" : "Failed";
            task.LastResult = result;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = success ? $"{task.Name} completed: {result}" : $"Task failed: {result}";
            return RedirectToAction(nameof(Backfills));
        }

        [HttpGet("")]
        [HttpGet("Dashboard")]
        public async Task<IActionResult> Index()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var today = DateTime.UtcNow;
            var sixMonthsAgo = new DateTime(today.Year, today.Month, 1).AddMonths(-5);
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var activeSessionThreshold = today.AddMinutes(-30);
            var signupThreshold = today.AddDays(-1);

            // 1. Batch Core Stats (Single round-trip for basic counts)
            var stats = await _context.Workplaces
                .Select(w => new {
                    TotalOrgs = _context.Workplaces.Count(),
                    ActiveOrgs = _context.Workplaces.Count(x => x.IsActive),
                    NewOrgs = _context.Workplaces.Count(x => x.CreatedAt > monthStart),
                    TotalUsers = _userManager.Users.Count(),
                    TotalDocs = _context.Documents.Count(),
                    ActiveSessions = _context.Users.Count(u => u.LastLogin > activeSessionThreshold),
                    RecentSignups = _context.Users.Count(u => u.CreatedAt > signupThreshold),
                    PendingInvites = _context.WorkplaceInvitations.Count(i => i.Status == "Pending")
                })
                .FirstOrDefaultAsync() ?? new { 
                    TotalOrgs = 0, ActiveOrgs = 0, NewOrgs = 0, TotalUsers = 0, 
                    TotalDocs = 0, ActiveSessions = 0, RecentSignups = 0, PendingInvites = 0 
                };

            // 2. Storage & Telemetry (Single query)
            long totalSizeBytes = await _context.DocumentVersions.SumAsync(dv => (long?)dv.FileSizeBytes) ?? 0;
            
            // 3. Storage Trends (SINGLE query for all 6 months)
            var trendDataRaw = await _context.DocumentVersions
                .Where(dv => dv.UploadedAt >= sixMonthsAgo)
                .GroupBy(dv => new { Month = dv.UploadedAt.Month, Year = dv.UploadedAt.Year })
                .Select(g => new { 
                    g.Key.Year, 
                    g.Key.Month, 
                    TotalSize = g.Sum(x => x.FileSizeBytes) 
                })
                .ToListAsync();

            // Format Trends for UI
            var labels = new List<string>();
            var dataPoints = new List<string>();
            for (int i = 5; i >= 0; i--)
            {
                var m = today.AddMonths(-i);
                labels.Add($"'{m:MMM}'");
                var size = trendDataRaw.FirstOrDefault(x => x.Month == m.Month && x.Year == m.Year)?.TotalSize ?? 0;
                // Use MB for best visibility with current data volumes
                dataPoints.Add((size / (1024.0 * 1024.0)).ToString("F1"));
            }

            // 4. Top Organizations (Efficient Join)
            var topOrgs = await _context.Workplaces
                .Select(w => new {
                    w.Name,
                    TotalSize = _context.Documents
                        .Where(d => d.WorkplaceID == w.WorkplaceID)
                        .Join(_context.DocumentVersions, d => d.DocumentID, dv => dv.DocumentID, (d, dv) => dv.FileSizeBytes)
                        .Sum(s => (long?)s) ?? 0
                })
                .OrderByDescending(x => x.TotalSize)
                .Take(5)
                .ToListAsync();

            // Populate ViewBags
            ViewBag.Analytics_StorageLabels = $"[{string.Join(", ", labels)}]";
            ViewBag.Analytics_StorageData = $"[{string.Join(", ", dataPoints)}]";
            ViewBag.Analytics_OrgDistributionLabels = "['New (This Month)', 'Other Active', 'Inactive']";
            ViewBag.Analytics_OrgDistributionData = $"[{stats.NewOrgs}, {stats.ActiveOrgs - stats.NewOrgs}, {stats.TotalOrgs - stats.ActiveOrgs}]";
            
            ViewBag.TopOrganizations = topOrgs.Select(o => new { o.Name, TotalSize = FormatBytes(o.TotalSize) }).ToList();
            ViewBag.TotalOrganizations = stats.TotalOrgs;
            ViewBag.TotalUsers = stats.TotalUsers;
            ViewBag.TotalDocuments = stats.TotalDocs;
            ViewBag.ActiveSessions = stats.ActiveSessions;
            ViewBag.SystemHealth = "Healthy";
            ViewBag.StorageUsed = FormatBytes(totalSizeBytes);
            ViewBag.StorageLimit = "10 TB";
            ViewBag.StoragePercent = ((double)totalSizeBytes / (10L * 1024 * 1024 * 1024 * 1024) * 100).ToString("F1");
            ViewBag.DatabaseLoad = "0.2"; // Static telemetry for performance
            ViewBag.RecentSignups = stats.RecentSignups;
            ViewBag.PendingInvitations = stats.PendingInvites;
            ViewBag.ActiveOrganizations = stats.ActiveOrgs;
            ViewBag.SuspendedOrganizations = stats.TotalOrgs - stats.ActiveOrgs;
            
            // 5. Advanced Analytics Calculations
            double avgStoragePerOrg = stats.TotalOrgs > 0 ? (double)totalSizeBytes / stats.TotalOrgs : 0;
            ViewBag.AvgStorageMB = (avgStoragePerOrg / (1024 * 1024)).ToString("F2");

            double growthVelocity = 0;
            string velocityText = "+0.0%";
            
            if (trendDataRaw.Count >= 1)
            {
                var sortedTrends = trendDataRaw.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToList();
                var latest = sortedTrends[0].TotalSize;
                
                if (sortedTrends.Count >= 2)
                {
                    var previous = sortedTrends[1].TotalSize;
                    if (previous > 0)
                    {
                        growthVelocity = ((double)(latest - previous) / previous) * 100;
                        velocityText = (growthVelocity >= 0 ? "+" : "") + growthVelocity.ToString("F1") + "%";
                    }
                    else if (latest > 0)
                    {
                        velocityText = "+100% (New)";
                    }
                }
                else if (latest > 0)
                {
                    // First month of data
                    velocityText = "+100% (Launch)";
                }
            }
            ViewBag.GrowthVelocity = velocityText;

            double retentionRate = stats.TotalOrgs > 0 ? ((double)stats.ActiveOrgs / stats.TotalOrgs) * 100 : 0;
            ViewBag.RetentionRate = retentionRate.ToString("F1") + "%";
            ViewBag.RetentionPercentage = retentionRate; // For progress bar
            ViewBag.ConcurrentPeak = stats.ActiveSessions; // Using current active as baseline for 'real' peek

            var adminRoleId = await _context.Roles.Where(r => r.Name == "Admin").Select(r => r.Id).FirstOrDefaultAsync();
            ViewBag.TotalAdmins = adminRoleId != 0 ? await _context.UserRoles.CountAsync(ur => ur.RoleId == adminRoleId) : 0;

            sw.Stop();
            ViewBag.ApiResponseTime = sw.ElapsedMilliseconds;

            return View();
        }

        [HttpGet("Analytics")]
        public async Task<IActionResult> Analytics()
        {
            // Now returns the specialized Analytics.cshtml view
            await Index(); 
            return View();
        }

        private const string SessionPendingPinKey = "Fm.SuperAdmin.PendingActionPin";
        private const string SessionPinSentTicksKey = "Fm.SuperAdmin.PinSentTicks";

        private static class SuperAdminActionPinPurposes
        {
            public const string CreateSuperAdmin = "CreateSuperAdmin";
            public const string ToggleUserStatus = "ToggleUserStatus";
        }

        private sealed class PendingSuperAdminPin
        {
            public string Code { get; set; } = "";
            public string Purpose { get; set; } = "";
            public int? TargetUserId { get; set; }
            public string? NormalizedInviteEmail { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
            public int FailedAttempts { get; set; }
        }

        private static string GenerateSixDigitPin() =>
            RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

        private static string NormalizeAuditSeverity(string? severity) =>
            severity?.Trim().ToLowerInvariant() switch
            {
                "critical" => "critical",
                "high" => "high",
                "normal" => "normal",
                _ => "all"
            };

        private static IQueryable<AuditLog> ApplyLoginAttemptsSeverity(IQueryable<AuditLog> securityRows, string severity) =>
            severity switch
            {
                "critical" => securityRows.Where(l =>
                    l.Action == "Login Locked" || l.Action == "Account Locked"
                    || l.Action == "CAPTCHA Failure" || l.Action == "MFA Failed"),
                "high" => securityRows.Where(l => l.Action == "Login Failed"),
                "normal" => securityRows.Where(l =>
                    l.Action != "Login Locked" && l.Action != "Account Locked"
                    && l.Action != "CAPTCHA Failure" && l.Action != "MFA Failed" && l.Action != "Login Failed"),
                _ => securityRows
            };

        private static IQueryable<AuditLog> ApplyPlatformAuditSeverity(IQueryable<AuditLog> query, string severity)
        {
            if (severity == "all")
                return query;

            if (severity == "critical")
            {
                return query.Where(l =>
                    (l.EntityType == "Security" && l.Action != null && (
                        l.Action == "Login Locked" || l.Action == "Account Locked"
                        || l.Action == "CAPTCHA Failure" || l.Action == "MFA Failed"))
                    || (l.Action != null && (
                        l.Action.Contains("Auto-Delete")
                        || l.Action == "API Key Revoked"
                        || l.Action == "Share Revoked"
                        || l.Action == "Invitation Deleted"
                        || l.Action == "Folder Deleted")));
            }

            if (severity == "high")
            {
                return query.Where(l =>
                    (l.EntityType == "Security" && l.Action == "Login Failed")
                    || (l.Action != null && (
                        l.Action == "Document Archived"
                        || l.Action == "User Role Changed"
                        || l.Action == "User Status Toggled")));
            }

            return query.Where(l =>
                !(
                    (l.EntityType == "Security" && l.Action != null && (
                        l.Action == "Login Locked" || l.Action == "Account Locked"
                        || l.Action == "CAPTCHA Failure" || l.Action == "MFA Failed" || l.Action == "Login Failed"))
                    || (l.Action != null && (
                        l.Action.Contains("Auto-Delete")
                        || l.Action == "API Key Revoked"
                        || l.Action == "Share Revoked"
                        || l.Action == "Invitation Deleted"
                        || l.Action == "Folder Deleted"
                        || l.Action == "Document Archived"
                        || l.Action == "User Role Changed"
                        || l.Action == "User Status Toggled"))
                ));
        }

        private static bool PinsEqual(string? submitted, string expected)
        {
            if (string.IsNullOrWhiteSpace(submitted) || string.IsNullOrEmpty(expected))
                return false;
            var a = submitted.Trim().Replace(" ", "", StringComparison.Ordinal);
            var b = expected.Trim();
            if (a.Length != b.Length)
                return false;
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
        }

        private static string MaskEmailForDisplay(string email)
        {
            var at = email.IndexOf('@');
            if (at < 0)
                return "***";
            if (at <= 1)
                return "***" + email[at..];
            return email[0] + "***" + email[at..];
        }

        private bool IsSuperAdminPinSendThrottled()
        {
            var raw = HttpContext.Session.GetString(SessionPinSentTicksKey);
            if (string.IsNullOrEmpty(raw) || !long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
                return false;
            try
            {
                var last = new DateTime(ticks, DateTimeKind.Utc);
                return DateTime.UtcNow - last < TimeSpan.FromSeconds(45);
            }
            catch
            {
                return false;
            }
        }

        private void MarkSuperAdminPinSent() =>
            HttpContext.Session.SetString(SessionPinSentTicksKey, DateTime.UtcNow.Ticks.ToString(NumberFormatInfo.InvariantInfo));

        private Task<bool> TryValidateAndConsumeSuperAdminPinAsync(string? submittedPin, string purpose, int? targetUserId, string? normalizedInviteEmail)
        {
            var raw = HttpContext.Session.GetString(SessionPendingPinKey);
            if (string.IsNullOrEmpty(raw))
                return Task.FromResult(false);

            PendingSuperAdminPin? pending;
            try
            {
                pending = JsonSerializer.Deserialize<PendingSuperAdminPin>(raw);
            }
            catch
            {
                HttpContext.Session.Remove(SessionPendingPinKey);
                return Task.FromResult(false);
            }

            if (pending == null || string.IsNullOrEmpty(pending.Code))
            {
                HttpContext.Session.Remove(SessionPendingPinKey);
                return Task.FromResult(false);
            }

            if (DateTimeOffset.UtcNow > pending.ExpiresAt)
            {
                HttpContext.Session.Remove(SessionPendingPinKey);
                return Task.FromResult(false);
            }

            if (!string.Equals(pending.Purpose, purpose, StringComparison.Ordinal))
                return Task.FromResult(false);

            if (purpose == SuperAdminActionPinPurposes.ToggleUserStatus)
            {
                if (!targetUserId.HasValue || pending.TargetUserId != targetUserId.Value)
                    return Task.FromResult(false);
            }

            if (purpose == SuperAdminActionPinPurposes.CreateSuperAdmin)
            {
                if (string.IsNullOrEmpty(normalizedInviteEmail)
                    || !string.Equals(pending.NormalizedInviteEmail, normalizedInviteEmail, StringComparison.Ordinal))
                    return Task.FromResult(false);
            }

            if (!PinsEqual(submittedPin, pending.Code))
            {
                pending.FailedAttempts++;
                if (pending.FailedAttempts >= 5)
                    HttpContext.Session.Remove(SessionPendingPinKey);
                else
                    HttpContext.Session.SetString(SessionPendingPinKey, JsonSerializer.Serialize(pending));
                return Task.FromResult(false);
            }

            HttpContext.Session.Remove(SessionPendingPinKey);
            HttpContext.Session.Remove(SessionPinSentTicksKey);
            return Task.FromResult(true);
        }

        private string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0.0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (number >= 1024 && counter < suffixes.Length - 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }
    }
}
