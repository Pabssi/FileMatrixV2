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

        /// <summary>
        /// Retrieves and filters the workplace audit trail based on action types 
        /// and text queries.
        /// </summary>
        public async Task<IActionResult> Index(string? query, string? actionFilter)
        {
            if (CurrentWorkplace == null) return RedirectToAction("Index", "Organizations", new { area = "" });

            var auditQuery = _context.AuditLogs
                .Include(l => l.User)
                .Where(l => l.WorkplaceID == CurrentWorkplace.WorkplaceID);

            if (!string.IsNullOrEmpty(actionFilter))
            {
                auditQuery = auditQuery.Where(l => l.Action == actionFilter);
            }

            if (!string.IsNullOrEmpty(query))
            {
                var lowerQuery = query.ToLower();
                auditQuery = auditQuery.Where(l => (l.Details != null && l.Details.ToLower().Contains(lowerQuery)) 
                                                 || (l.EntityType != null && l.EntityType.ToLower().Contains(lowerQuery)));
            }

            var logs = await auditQuery
                .OrderByDescending(l => l.PerformedAt)
                .Take(100)
                .ToListAsync();

            // Stats for the view
            ViewBag.TotalEvents = await _context.AuditLogs.CountAsync(l => l.WorkplaceID == CurrentWorkplace.WorkplaceID);
            ViewBag.DocumentActions = await _context.AuditLogs.CountAsync(l => l.WorkplaceID == CurrentWorkplace.WorkplaceID && l.EntityType == "Document");
            ViewBag.UserActions = await _context.AuditLogs.CountAsync(l => l.WorkplaceID == CurrentWorkplace.WorkplaceID && l.EntityType == "User");
            ViewBag.TodayEvents = await _context.AuditLogs.CountAsync(l => l.WorkplaceID == CurrentWorkplace.WorkplaceID && l.PerformedAt >= DateTime.UtcNow.Date);

            ViewBag.Query = query;
            ViewBag.ActionFilter = actionFilter;
            ViewBag.ActionTypes = await _context.AuditLogs
                .Where(l => l.WorkplaceID == CurrentWorkplace.WorkplaceID)
                .Select(l => l.Action)
                .Distinct()
                .ToListAsync();

            return View(logs);
        }
    }
}

