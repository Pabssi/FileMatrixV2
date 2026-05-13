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
    /// DocumentsController.Actions: Document State & Metadata Management.
    /// 
    /// RESPONSIBILITY: Handles non-content changes such as favoriting, status updates, 
    /// organizational categorization, and archiving logic.
    /// </summary>
    public partial class DocumentsController
    {
        /// <summary>
        /// Toggles the 'IsFavorite' flag. This is a per-document, per-workplace setting.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ToggleFavorite(int id)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();

            var doc = await _context.Documents.FindAsync(id);
            if (doc == null || doc.WorkplaceID != CurrentWorkplace.WorkplaceID) return NotFound();

            doc.IsFavorite = !doc.IsFavorite;
            await _context.SaveChangesAsync();

            return Json(new { success = true, isFavorite = doc.IsFavorite });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int id, string status)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 2) return Unauthorized();

            var doc = await _context.Documents.FindAsync(id);
            if (doc == null || doc.WorkplaceID != CurrentWorkplace.WorkplaceID) return NotFound();

            doc.Status = status;
            await _context.SaveChangesAsync();

            return Json(new { success = true, status = doc.Status });
        }

        /// <summary>
        /// Moves a document between organizational categories and logs the transition for audit trail.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> MoveToCategory(int id, int? categoryId)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 2) return Unauthorized();

            // Simple Query: Find the document we want to move, including its current category name.
            var doc = await _context.Documents
                .Include(d => d.Category)
                .FirstOrDefaultAsync(d => d.DocumentID == id);

            if (doc == null || doc.WorkplaceID != CurrentWorkplace.WorkplaceID) return NotFound();

            string oldCategory = doc.Category?.Name ?? "Uncategorized";
            doc.CategoryID = categoryId;
            doc.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Fetch new category name
            var newCatName = "Uncategorized";
            if (categoryId.HasValue)
            {
                var cat = await _context.Categories.FindAsync(categoryId.Value);
                newCatName = cat?.Name ?? "Uncategorized";
            }

            // Log activity
            var auditLog = new AuditLog
            {
                WorkplaceID = CurrentWorkplace.WorkplaceID,
                UserID = CurrentMembership.UserID,
                Action = "Document Moved",
                EntityType = "Document",
                EntityID = doc.DocumentID,
                Details = $"Moved document '{doc.Title}' from '{oldCategory}' to '{newCatName}'",
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                PerformedAt = DateTime.UtcNow
            };
            _context.AuditLogs.Add(auditLog);
            await _context.SaveChangesAsync();

            return Json(new { success = true, newCategoryName = newCatName });
        }

        /// <summary>
        /// Soft-archives a document. Archived documents are preserved but generally 
        /// excluded from standard index queries.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Archive(int id)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 2) return Forbid(); // Admin(1) or Editor(2) only

            var doc = await _context.Documents.FindAsync(id);
            if (doc == null || doc.WorkplaceID != CurrentWorkplace.WorkplaceID) return NotFound();

            doc.Status = "Archived";
            doc.ArchivedAt = DateTime.UtcNow;
            doc.RetentionNoticeSent = false;
            await _context.SaveChangesAsync();

            // Log activity
            var log = new AuditLog
            {
                WorkplaceID = CurrentWorkplace.WorkplaceID,
                Action = "Document Archived",
                EntityType = "Document",
                EntityID = doc.DocumentID,
                UserID = CurrentMembership.UserID,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                PerformedAt = DateTime.UtcNow,
                Details = $"Archived document: {doc.Title}"
            };
            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> Restore(int id)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 2) return Forbid(); // Admin(1) or Editor(2) only

            var doc = await _context.Documents.FindAsync(id);
            if (doc == null || doc.WorkplaceID != CurrentWorkplace.WorkplaceID) return NotFound();

            doc.Status = "Published"; // Default to published when restored
            doc.ArchivedAt = null;
            await _context.SaveChangesAsync();

            // Log activity
            var log = new AuditLog
            {
                WorkplaceID = CurrentWorkplace.WorkplaceID,
                Action = "Document Restored",
                EntityType = "Document",
                EntityID = doc.DocumentID,
                UserID = CurrentMembership.UserID,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                PerformedAt = DateTime.UtcNow,
                Details = $"Restored document: {doc.Title}"
            };
            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

    }
}
