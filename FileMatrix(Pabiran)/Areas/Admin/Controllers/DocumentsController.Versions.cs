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
    public partial class DocumentsController
    {
        [HttpGet]
        public async Task<IActionResult> VersionHistory(int id)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();

            var doc = await _context.Documents.FindAsync(id);
            if (doc == null || doc.WorkplaceID != CurrentWorkplace.WorkplaceID) return NotFound();

            var versions = await _context.DocumentVersions
                .Where(v => v.DocumentID == id)
                .OrderByDescending(v => v.VersionNumber)
                .ToListAsync();

            var uploaderIds = versions.Select(v => v.UploadedByUserID).Distinct().ToList();
            var uploaders = await _context.Users
                .Where(u => uploaderIds.Contains(u.UserID))
                .ToDictionaryAsync(u => u.UserID, u => u.DisplayName ?? u.Username ?? "Unknown");

            var result = versions.Select(v => new {
                versionID = v.VersionID,
                versionNumber = v.VersionNumber.ToString("0.0"),
                fileName = v.FileName,
                uploadedAt = v.UploadedAt.ToString("MMM dd, yyyy HH:mm"),
                uploadedBy = uploaders.ContainsKey(v.UploadedByUserID ?? 0) ? uploaders[v.UploadedByUserID ?? 0] : "Unknown",
                changeNote = v.ChangeNote,
                fileSize = FormatBytes(v.FileSizeBytes),
                isCurrent = v.VersionID == doc.CurrentVersionID,
                canRestore = CurrentMembership.RoleID <= 2
            });

            return Json(new { success = true, versions = result });
        }

        [HttpPost]
        [RequestSizeLimit(104857600)] // 100MB Limit
        public async Task<IActionResult> UploadVersion(int documentId, IFormFile file, string changeNote)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 2) return Forbid(); // Admin(1) or Editor(2) only
            
            try
            {
                var version = await _documentService.UploadVersionAsync(documentId, file, changeNote, CurrentMembership.UserID);
                return Json(new { success = true, version = version.VersionNumber.ToString("0.0") });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RestoreVersion(int versionId)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 2) return Forbid(); // Admin(1) or Editor(2) only

            var oldVersion = await _context.DocumentVersions
                .Include(v => v.Document)
                .FirstOrDefaultAsync(v => v.VersionID == versionId);

            if (oldVersion == null || oldVersion.Document.WorkplaceID != CurrentWorkplace.WorkplaceID)
                return NotFound();

            // Create a new version based on the old one
            var currentLatest = await _context.DocumentVersions
                .Where(v => v.DocumentID == oldVersion.DocumentID)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync();

            decimal nextVersion = (currentLatest?.VersionNumber ?? 1.0m) + 1.0m;

            var newVersion = new DocumentVersion
            {
                DocumentID = oldVersion.DocumentID,
                VersionNumber = nextVersion,
                FileName = oldVersion.FileName,
                FilePath = oldVersion.FilePath,
                FileSizeBytes = oldVersion.FileSizeBytes,
                MimeType = oldVersion.MimeType,
                UploadedByUserID = CurrentMembership.UserID,
                UploadedAt = DateTime.UtcNow,
                ChangeNote = $"Restored from v{oldVersion.VersionNumber:0.0}"
            };

            _context.DocumentVersions.Add(newVersion);
            await _context.SaveChangesAsync();

            // Update document
            oldVersion.Document.CurrentVersionID = newVersion.VersionID;
            oldVersion.Document.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Log activity
            var log = new AuditLog
            {
                WorkplaceID = CurrentWorkplace.WorkplaceID,
                Action = "Version Restored",
                EntityType = "Document",
                EntityID = oldVersion.DocumentID,
                UserID = CurrentMembership.UserID,
                PerformedAt = DateTime.UtcNow,
                Details = $"Restored document to v{oldVersion.VersionNumber:0.0}"
            };
            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();

            return Json(new { success = true, version = nextVersion.ToString("0.0") });
        }
    }
}
