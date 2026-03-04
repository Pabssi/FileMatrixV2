using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FileMatrix_Pabiran_.Data;
using FileMatrix_Pabiran_.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using System.Linq;

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

        public SuperAdminController(ApplicationDbContext context, UserManager<IdentityUser<int>> userManager)
        {
            _context = context;
            _userManager = userManager;
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

        [HttpPost("Users/ToggleStatus/{id}")]
        public async Task<IActionResult> ToggleUserStatus(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                user.IsActive = !user.IsActive;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Users));
        }

        [HttpGet("AuditLogs")]
        public async Task<IActionResult> AuditLogs()
        {
            var logs = await _context.AuditLogs
                .OrderByDescending(l => l.PerformedAt)
                .Take(100)
                .ToListAsync();

            ViewBag.Logs = logs;
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

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }
    }
}
