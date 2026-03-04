using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using FileMatrix_Pabiran_.Areas.Admin.Models;

namespace FileMatrix_Pabiran_.Areas.Admin.Controllers
{
    [Area("Admin")]
    /// <summary>
    /// UsersController: Workplace-Level Member Management.
    /// 
    /// RESPONSIBILITY: Manages the collection of users within the current workplace.
    /// Handles role display and user activation/deactivation for the specific organization.
    /// </summary>
    public class UsersController : BaseAdminController
    {
        public UsersController(FileMatrix_Pabiran_.Data.ApplicationDbContext context) : base(context)
        {
        }

        /// <summary>
        /// Lists all members of the current workplace, resolving their profiles and local roles.
        /// </summary>
        public async Task<IActionResult> Index()
        {
            if (CurrentWorkplace == null) return RedirectToAction("Index", "Organizations", new { area = "" });

            var membersRaw = await _context.WorkplaceMembers
                .Where(m => m.WorkplaceID == CurrentWorkplace.WorkplaceID)
                .ToListAsync();

            var vm = new UserManagementViewModel
            {
                WorkplaceID = CurrentWorkplace.WorkplaceID,
                WorkplaceName = CurrentWorkplace.Name ?? "My Workspace",
                Members = new List<WorkplaceMemberItemViewModel>()
            };

            foreach (var m in membersRaw)
            {
                var user = await _context.Users.FindAsync(m.UserID);
                if (user != null)
                {
                    vm.Members.Add(new WorkplaceMemberItemViewModel
                    {
                        UserID = user.UserID,
                        Username = user.Username ?? "",
                        Email = user.Email ?? "",
                        RoleName = m.RoleID == 1 ? "Admin" : (m.RoleID == 2 ? "Editor" : "Viewer"),
                        JoinedAt = m.JoinedAt,
                        IsActive = user.IsActive
                    });
                }
            }

            return View(vm);
        }

        /// <summary>
        /// Toggles the 'IsActive' status for a user, effectively enabling or disabling 
        /// their access to this specific workplace.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ToggleStatus(int userId)
        {
            if (CurrentWorkplace == null) return Unauthorized();

            var workplaceUser = await _context.Users.FindAsync(userId);
            if (workplaceUser != null)
            {
                // Verify they are actually in this workplace
                var isMember = await _context.WorkplaceMembers.AnyAsync(m => m.WorkplaceID == CurrentWorkplace.WorkplaceID && m.UserID == userId);
                if (isMember)
                {
                    workplaceUser.IsActive = !workplaceUser.IsActive;
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Status for user '{workplaceUser.Username}' has been updated.";
                }
            }

            return RedirectToAction(nameof(Index));
        }
    }
}

