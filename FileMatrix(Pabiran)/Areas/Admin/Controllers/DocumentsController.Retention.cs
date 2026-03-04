using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using FileMatrix_Pabiran_.Models;
using FileMatrix_Pabiran_.Areas.Admin.Models;
using Microsoft.AspNetCore.Http;
using FileMatrix_Pabiran_.Data;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System;
using System.IO;

namespace FileMatrix_Pabiran_.Areas.Admin.Controllers
{
    /// <summary>
    /// DocumentsController.Retention: Document Lifecycle Management.
    /// 
    /// RESPONSIBILITY: Manages automated persistence rules (Auto-Archive/Auto-Delete) 
    /// for a specific workplace. 
    /// NOTE: These settings are restricted to the Workplace Administrator.
    /// </summary>
    public partial class DocumentsController
    {
        /// <summary>
        /// Retrieves or initializes the retention policy for the current workspace.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Retention()
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 1) return Forbid(); // Admin only (RoleID 1)

            var policy = await _context.RetentionPolicies
                .FirstOrDefaultAsync(p => p.WorkplaceID == CurrentWorkplace.WorkplaceID);

            if (policy == null)
            {
                policy = new RetentionPolicy { WorkplaceID = CurrentWorkplace.WorkplaceID };
            }

            return View(policy);
        }

        [HttpPost]
        public async Task<IActionResult> Retention(RetentionPolicy model)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 1) return Forbid(); // Admin only

            var policy = await _context.RetentionPolicies
                .FirstOrDefaultAsync(p => p.WorkplaceID == CurrentWorkplace.WorkplaceID);

            if (policy == null)
            {
                policy = new RetentionPolicy { WorkplaceID = CurrentWorkplace.WorkplaceID };
                _context.RetentionPolicies.Add(policy);
            }

            policy.AutoArchiveAfterDays = model.AutoArchiveAfterDays;
            policy.AutoDeleteAfterDays = model.AutoDeleteAfterDays;
            policy.IsEnabled = model.IsEnabled;
            policy.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Retention policy updated successfully.";
            return RedirectToAction(nameof(Retention));
        }
    }
}
