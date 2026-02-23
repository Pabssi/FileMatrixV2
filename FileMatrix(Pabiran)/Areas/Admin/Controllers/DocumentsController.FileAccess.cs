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
        [AllowAnonymous]
        public async Task<IActionResult> Download(int id, string? token = null, int? versionId = null)
        {
            // Privacy Standard: SuperAdmins are blocked from document content access
            if (User.IsInRole("SuperAdmin")) return Forbid();

            var doc = await _context.Documents.FindAsync(id);
            if (doc == null) return NotFound();

            bool isAuthorized = false;

            // Check token
            if (!string.IsNullOrEmpty(token) && doc.PublicShareToken == token)
            {
                isAuthorized = true;
            }
            else if (CurrentWorkplace != null && CurrentMembership != null && doc.WorkplaceID == CurrentWorkplace.WorkplaceID)
            {
                isAuthorized = true;
            }
            else if (User.Identity?.IsAuthenticated == true)
            {
                var email = User.FindFirstValue(ClaimTypes.Email);
                var userId = await _context.Users.Where(u => u.Email == email).Select(u => u.UserID).FirstOrDefaultAsync();
                if (userId > 0)
                {
                    isAuthorized = await _context.DocumentPermissions.AnyAsync(p => p.DocumentID == id && p.UserID == userId);
                }
            }

            if (!isAuthorized) return Challenge();

            DocumentVersion? version = null;
            if (versionId.HasValue)
            {
                version = await _context.DocumentVersions
                    .FirstOrDefaultAsync(v => v.VersionID == versionId && v.DocumentID == id);
            }
            else
            {
                version = await _context.DocumentVersions
                    .Where(v => v.DocumentID == doc.DocumentID)
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefaultAsync();
            }

            if (version == null || version.FilePath == null) return NotFound("File version not found.");

            // Construct physical path
            var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var physicalPath = Path.Combine(webRoot, version.FilePath.Replace("/", "\\"));

            if (!System.IO.File.Exists(physicalPath)) return NotFound("Physical file missing.");

            var fileBytes = await System.IO.File.ReadAllBytesAsync(physicalPath);
            return File(fileBytes, version.MimeType ?? "application/octet-stream", version.FileName);
        }

        [AllowAnonymous]
        public async Task<IActionResult> ViewFile(int id, string? token = null, int? versionId = null)
        {
            // Privacy Standard: SuperAdmins are blocked from document content access
            if (User.IsInRole("SuperAdmin")) return Forbid();

            var doc = await _context.Documents.FindAsync(id);
            if (doc == null) return NotFound();

            bool isAuthorized = false;

            // Check token
            if (!string.IsNullOrEmpty(token) && doc.PublicShareToken == token)
            {
                isAuthorized = true;
            }
            else if (CurrentWorkplace != null && CurrentMembership != null && doc.WorkplaceID == CurrentWorkplace.WorkplaceID)
            {
                isAuthorized = true;
            }
            else if (User.Identity?.IsAuthenticated == true)
            {
                var email = User.FindFirstValue(ClaimTypes.Email);
                var userId = await _context.Users.Where(u => u.Email == email).Select(u => u.UserID).FirstOrDefaultAsync();
                if (userId > 0)
                {
                    isAuthorized = await _context.DocumentPermissions.AnyAsync(p => p.DocumentID == id && p.UserID == userId);
                }
            }

            if (!isAuthorized) return Challenge();

            DocumentVersion? version = null;
            if (versionId.HasValue)
            {
                version = await _context.DocumentVersions
                    .FirstOrDefaultAsync(v => v.VersionID == versionId && v.DocumentID == id);
            }
            else
            {
                version = await _context.DocumentVersions
                    .Where(v => v.DocumentID == doc.DocumentID)
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefaultAsync();
            }

            if (version == null || version.FilePath == null) return NotFound("File version not found.");

            var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var physicalPath = Path.Combine(webRoot, version.FilePath.Replace("/", "\\"));

            if (!System.IO.File.Exists(physicalPath)) return NotFound("Physical file missing.");

            var fileBytes = await System.IO.File.ReadAllBytesAsync(physicalPath);
            
            var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(version.FileName, out var contentType))
            {
                contentType = version.MimeType ?? "application/octet-stream";
            }

            Response.Headers.Append("Content-Disposition", "inline; filename=\"" + version.FileName + "\"");
            return File(fileBytes, contentType);
        }
    }
}
