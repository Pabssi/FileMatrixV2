using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FileMatrix_Pabiran_.Areas.Admin.Controllers;
using FileMatrix_Pabiran_.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using System.Collections.Generic;

namespace FileMatrix_Pabiran_.Areas.Admin.Controllers
{
    /// <summary>
    /// NotificationsController: In-App User Alerting System.
    /// 
    /// RESPONSIBILITY: Manages the delivery and state (Read/Unread) of 
    /// notifications for a specific user within a specific workplace.
    /// </summary>
    public class NotificationsController : BaseAdminController
    {
        public NotificationsController(ApplicationDbContext context) : base(context)
        {
        }

        /// <summary>
        /// Retrieves the notification stream, hard-partitioned by Workplace and Recipient.
        /// </summary>
        public async Task<IActionResult> Index(string filter = "all")
        {
            if (CurrentWorkplace == null)
            {
                return RedirectToAction("Index", "Organizations", new { area = "" });
            }

            var email = User.FindFirstValue(ClaimTypes.Email);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null) return View(new List<FileMatrix_Pabiran_.Models.Notification>());

            var query = _context.Notifications
                .Where(n => n.WorkplaceID == CurrentWorkplace.WorkplaceID && n.RecipientUserID == user.UserID)
                .OrderByDescending(n => n.CreatedAt);

            List<FileMatrix_Pabiran_.Models.Notification> notifications = await query.ToListAsync();

            if (filter == "unread")
            {
                notifications = notifications.Where(n => !n.IsSent).ToList();
            }

            return View(notifications);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllAsRead()
        {
            if (CurrentWorkplace == null) return Json(new { success = false });

            var email = User.FindFirstValue(ClaimTypes.Email);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null) return Json(new { success = false });

            var unread = await _context.Notifications
                .Where(n => n.WorkplaceID == CurrentWorkplace.WorkplaceID && n.RecipientUserID == user.UserID && !n.IsSent)
                .ToListAsync();

            foreach (var n in unread)
            {
                n.IsSent = true;
                n.SentAt = System.DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}
