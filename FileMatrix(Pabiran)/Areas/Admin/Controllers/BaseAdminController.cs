using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using FileMatrix_Pabiran_.Data;
using FileMatrix_Pabiran_.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace FileMatrix_Pabiran_.Areas.Admin.Controllers
{
    /// <summary>
    /// BaseAdminController: The Security & Context Hub for the Admin Area.
    /// 
    /// DESIGN PATTERN: Session-Scoped Isolation. 
    /// This controller ensures that ALL administrative actions are strictly contained within a "Current Workplace".
    /// It automatically resolves:
    /// 1. The Active Workplace (from cookies or membership).
    /// 2. The User's Role within that specific workplace.
    /// 3. Security blocks (suspensions, SuperAdmin restrictions).
    /// </summary>
    [Area("Admin")]
    public abstract class BaseAdminController : Controller
    {
        protected readonly ApplicationDbContext _context;
        protected Workplace? CurrentWorkplace { get; private set; }
        protected WorkplaceMember? CurrentMembership { get; private set; }

        public BaseAdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // Respect [AllowAnonymous] attribute for public sharing
            bool allowAnonymous = context.ActionDescriptor.EndpointMetadata
                .Any(em => em is AllowAnonymousAttribute);

            // Always attempt to load user context if they are authenticated
            if (User.Identity?.IsAuthenticated == true)
            {
                var email = User.FindFirstValue(ClaimTypes.Email)?.ToLowerInvariant();
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email);

                if (user != null)
                {
                    // Governance: Check if user account is suspended
                    if (!user.IsActive)
                    {
                        TempData["ErrorMessage"] = "Your account has been suspended by a platform administrator.";
                        context.Result = new RedirectToActionResult("Index", "Home", new { area = "" });
                        return;
                    }

                    var memberships = await _context.WorkplaceMembers
                        .Where(m => m.UserID == user.UserID)
                        .OrderByDescending(m => m.JoinedAt)
                        .ToListAsync();

                    if (memberships.Any())
                    {
                        // Check for LastWorkplaceID cookie
                        if (Request.Cookies.TryGetValue("LastWorkplaceID", out string? lastWpIdStr) && int.TryParse(lastWpIdStr, out int lastWpId))
                        {
                            var preferred = memberships.FirstOrDefault(m => m.WorkplaceID == lastWpId);
                            if (preferred != null)
                            {
                                // Verify preferred workplace is active
                                var wp = await _context.Workplaces.FindAsync(lastWpId);
                                if (wp != null && wp.IsActive)
                                {
                                    CurrentMembership = preferred;
                                    CurrentWorkplace = wp;
                                }
                            }
                        }

                        // Fallback to most recent ACTIVE workplace
                        if (CurrentMembership == null)
                        {
                            // Optimized: Find the first membership where the workplace is active
                            foreach (var member in memberships)
                            {
                                var wp = await _context.Workplaces.FindAsync(member.WorkplaceID);
                                if (wp != null && wp.IsActive)
                                {
                                    CurrentMembership = member;
                                    CurrentWorkplace = wp;
                                    break;
                                }
                            }
                        }

                        // If we found an active workplace, populate ViewBag
                        if (CurrentWorkplace != null)
                        {
                            // Populate list for switcher
                            var workplaceIds = memberships.Select(m => m.WorkplaceID).ToList();
                            var myWorkplaces = await _context.Workplaces
                                .Where(w => workplaceIds.Contains(w.WorkplaceID))
                                .Select(w => new { w.WorkplaceID, w.Name, w.IsActive })
                                .ToListAsync();

                            ViewBag.CurrentWorkplace = CurrentWorkplace;
                            ViewBag.CurrentMembership = CurrentMembership;
                            ViewBag.MyWorkplaces = myWorkplaces;
                        }
                        else 
                        {
                            // No active workplaces found among memberships
                            TempData["ErrorMessage"] = "You do not have access to any active workplaces.";
                            context.Result = new RedirectToActionResult("Index", "Organizations", new { area = "" });
                            return;
                        }
                    }
                }
            }

            // If it's NOT an anonymous action, we enforce authentication and workplace membership
            if (!allowAnonymous)
            {
                if (User.Identity?.IsAuthenticated != true)
                {
                    context.Result = new RedirectToActionResult("Index", "Home", new { area = "" });
                    return;
                }

                if (CurrentWorkplace == null)
                {
                    // If they have no workplaces, send them to create one
                    context.Result = new RedirectToActionResult("Index", "Organizations", new { area = "" });
                    return;
                }
            }

            await base.OnActionExecutionAsync(context, next);
        }
    }
}
