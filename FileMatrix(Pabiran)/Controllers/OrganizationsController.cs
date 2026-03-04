using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FileMatrix_Pabiran_.Models;
using Microsoft.AspNetCore.Mvc;

namespace FileMatrix_Pabiran_.Controllers
{
    /// <summary>
    /// OrganizationsController: The Multi-Tenancy Engine.
    /// 
    /// RESPONSIBILITY: Manages the lifecycle of 'Workplaces' (Organizations) 
    /// and the 'Join' workflow for invited members.
    /// </summary>
    public class OrganizationsController : Controller
    {
        private readonly FileMatrix_Pabiran_.Data.ApplicationDbContext _db;

        public OrganizationsController(FileMatrix_Pabiran_.Data.ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var email = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(email)) return View();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null) return View();

            var memberships = await _db.WorkplaceMembers
                .Where(m => m.UserID == user.UserID)
                .Select(m => new { 
                    m.WorkplaceID, 
                    m.Workplace.Name, 
                    m.Workplace.IsActive,
                    RoleName = m.RoleID == 1 ? "Administrator" : m.RoleID == 2 ? "Editor" : "Viewer"
                })
                .ToListAsync();

            ViewBag.UserWorkspaces = memberships;
            return View();
        }

        [HttpGet]
        public IActionResult Create() => View(new FileMatrix_Pabiran_.Models.Workplace());

        /// <summary>
        /// Creates a new workplace and automatically assigns the creator as 
        /// the primary Administrator (RoleID 1).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(FileMatrix_Pabiran_.Models.Workplace model)
        {
            if (!ModelState.IsValid) return View(model);

            // Get current user from the custom Users table
            var email = User.FindFirstValue(ClaimTypes.Email);
            var currentUser = email != null
                ? _db.Users.FirstOrDefault(u => u.Email == email)
                : null;

            model.CreatedAt = DateTime.UtcNow;
            if (currentUser != null)
            {
                model.CreatedByUserID = currentUser.UserID;
            }

            _db.Workplaces.Add(model);
            await _db.SaveChangesAsync();

            // Ensure the creator is a member/owner of the new organization
            if (currentUser != null)
            {
                var member = new FileMatrix_Pabiran_.Models.WorkplaceMember
                {
                    WorkplaceID = model.WorkplaceID,
                    UserID = currentUser.UserID,
                    RoleID = 1, // 1 = Admin/Owner
                    JoinedAt = DateTime.UtcNow
                };
                _db.WorkplaceMembers.Add(member);
                await _db.SaveChangesAsync();
            }

            // redirect to dashboard
            return RedirectToAction("Index", "Admin", new { area = "Admin" });
        }

        public IActionResult Switch() => View();
        public IActionResult Members() => View();
        public IActionResult Invite() => View();
        [HttpGet]
        public async Task<IActionResult> Join(string? token)
        {
            if (string.IsNullOrEmpty(token)) return View();

            return await ProcessJoin(token);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("Join")]
        public async Task<IActionResult> JoinPost(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                TempData["ErrorMessage"] = "Please enter an invitation code.";
                return View();
            }

            return await ProcessJoin(token);
        }

        /// <summary>
        /// The 'Invitation Processor': Validates tokens, checks usage limits, 
        /// enforces email restrictions, and handles role promotes/updates for 
        /// existing members.
        /// </summary>
        private async Task<IActionResult> ProcessJoin(string token)
        {
            var invitation = _db.WorkplaceInvitations
                .FirstOrDefault(i => i.Token == token);

            if (invitation == null)
            {
                TempData["ErrorMessage"] = "Invalid invitation token.";
                return RedirectToAction("Join");
            }

            // check if authenticated
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                TempData["ErrorMessage"] = "Please sign in or create an account to accept this invitation.";
                // redirect to login with returnUrl
                return RedirectToAction("Login", "Account", new { returnUrl = Url.Action("Join", "Organizations", new { token = token }) });
            }

            var email = User.FindFirstValue(ClaimTypes.Email);
            var currentUser = _db.Users.FirstOrDefault(u => u.Email == email);

            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "User profile not found.";
                return RedirectToAction("Join");
            }

            // Validation
            if (invitation.Status != "pending")
            {
                TempData["ErrorMessage"] = "This invitation is no longer active.";
                return RedirectToAction("Join");
            }

            if (invitation.ExpiresAt != null && invitation.ExpiresAt < DateTime.UtcNow)
            {
                TempData["ErrorMessage"] = "This invitation has expired.";
                return RedirectToAction("Join");
            }

            if (invitation.UsageLimit != null && invitation.UsageCount >= invitation.UsageLimit)
            {
                TempData["ErrorMessage"] = "This invitation has reached its usage limit.";
                return RedirectToAction("Join");
            }

            if (!string.IsNullOrEmpty(invitation.Email) && !string.Equals(invitation.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "This invitation is restricted to another email address.";
                return RedirectToAction("Join");
            }

            // Check if already a member
            var existingMembership = _db.WorkplaceMembers.FirstOrDefault(m => m.WorkplaceID == invitation.WorkplaceID && m.UserID == currentUser.UserID);
            if (existingMembership != null)
            {
                if (existingMembership.RoleID == invitation.RoleID)
                {
                    TempData["SuccessMessage"] = "You are already a member of this workspace with this role.";
                    return RedirectToAction("Index", "Admin", new { area = "Admin" });
                }
                
                // Allow role update (e.g. promoting or switching role via new invitation)
                existingMembership.RoleID = invitation.RoleID;
                existingMembership.JoinedAt = DateTime.UtcNow; // Update join time to make it the "active" context
                invitation.UsageCount++;
                
                await _db.SaveChangesAsync();
                
                var roleName = invitation.RoleID == 1 ? "Administrator" : invitation.RoleID == 2 ? "Editor" : "Viewer";
                TempData["SuccessMessage"] = $"Your role has been updated to {roleName}!";
                return RedirectToAction("Index", "Admin", new { area = "Admin" });
            }

            // Join
            var member = new WorkplaceMember
            {
                WorkplaceID = invitation.WorkplaceID,
                UserID = currentUser.UserID,
                RoleID = invitation.RoleID,
                JoinedAt = DateTime.UtcNow
            };

            _db.WorkplaceMembers.Add(member);
            invitation.UsageCount++;

            if (invitation.UsageLimit != null && invitation.UsageCount >= invitation.UsageLimit)
            {
                invitation.Status = "used";
            }

            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = "Successfully joined the workspace!";
            return RedirectToAction("Index", "Admin", new { area = "Admin" });
        }
    }
}
