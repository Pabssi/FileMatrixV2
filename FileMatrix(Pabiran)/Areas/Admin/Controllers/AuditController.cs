using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FileMatrix_Pabiran_.Models;
using FileMatrix_Pabiran_.Data;

namespace FileMatrix_Pabiran_.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Route("Admin/Audit")]
    /// <summary>
    /// AuditController: Workplace Activity Oversight.
    /// 
    /// RESPONSIBILITY: Provides a searchable interface for administrators to 
    /// monitor all significant events (Document changes, user status toggles) 
    /// within their workplace.
    /// </summary>
    public class AuditController : BaseAdminController
    {
        public AuditController(ApplicationDbContext context) : base(context)
        {
        }

        private const int AuditPageSize = 20;

        /// <summary>
        /// Retrieves and filters the workplace audit trail based on action types 
        /// and text queries.
        /// </summary>
        public async Task<IActionResult> Index(string? query, string? actionFilter, int page = 1)
        {
            if (CurrentWorkplace == null) return RedirectToAction("Index", "Organizations", new { area = "" });

            // Platform sign-in rows use EntityType "Security". Exclude those from tenant audit (including legacy NULL EntityType rows).
            var auditQuery = _context.AuditLogs
                .Include(l => l.User)
                .Where(l => l.WorkplaceID == CurrentWorkplace.WorkplaceID && (l.EntityType == null || l.EntityType != "Security"));

            if (!string.IsNullOrWhiteSpace(actionFilter))
            {
                auditQuery = auditQuery.Where(l => l.Action == actionFilter);
            }

            if (!string.IsNullOrEmpty(query))
            {
                var lowerQuery = query.ToLower();
                // Match details, entity, action, and actor (same signals the search placeholder promises).
                auditQuery = auditQuery.Where(l =>
                    (l.Details != null && l.Details.ToLower().Contains(lowerQuery))
                    || (l.EntityType != null && l.EntityType.ToLower().Contains(lowerQuery))
                    || (l.Action != null && l.Action.ToLower().Contains(lowerQuery))
                    || (l.User != null && l.User.DisplayName != null && l.User.DisplayName.ToLower().Contains(lowerQuery))
                    || (l.User != null && l.User.Username != null && l.User.Username.ToLower().Contains(lowerQuery))
                    || (l.User != null && l.User.Email != null && l.User.Email.ToLower().Contains(lowerQuery)));
            }

            var totalFiltered = await auditQuery.CountAsync();
            var totalPages = totalFiltered == 0 ? 1 : (int)Math.Ceiling(totalFiltered / (double)AuditPageSize);
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var logs = await auditQuery
                .OrderByDescending(l => l.PerformedAt)
                .Skip((page - 1) * AuditPageSize)
                .Take(AuditPageSize)
                .ToListAsync();

            // Stats for the view
            ViewBag.TotalEvents = await _context.AuditLogs.CountAsync(l => l.WorkplaceID == CurrentWorkplace.WorkplaceID && (l.EntityType == null || l.EntityType != "Security"));
            ViewBag.DocumentActions = await _context.AuditLogs.CountAsync(l => l.WorkplaceID == CurrentWorkplace.WorkplaceID && l.EntityType == "Document");
            ViewBag.UserActions = await _context.AuditLogs.CountAsync(l => l.WorkplaceID == CurrentWorkplace.WorkplaceID && l.EntityType == "User");
            ViewBag.TodayEvents = await _context.AuditLogs.CountAsync(l => l.WorkplaceID == CurrentWorkplace.WorkplaceID && (l.EntityType == null || l.EntityType != "Security") && l.PerformedAt >= DateTime.UtcNow.Date);

            ViewBag.Query = query;
            ViewBag.ActionFilter = actionFilter;
            ViewBag.AuditPage = page;
            ViewBag.AuditTotalPages = totalPages;
            ViewBag.AuditTotalFiltered = totalFiltered;
            ViewBag.AuditPageSize = AuditPageSize;
            ViewBag.ActionTypes = await _context.AuditLogs
                .Where(l => l.WorkplaceID == CurrentWorkplace.WorkplaceID && (l.EntityType == null || l.EntityType != "Security") && l.Action != null && l.Action != "")
                .Select(l => l.Action!)
                .Distinct()
                .OrderBy(a => a)
                .ToListAsync();

            return View(logs);
        }
    }
}

