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
    /// DocumentsController.FileAccess: The Security & Content Gateway.
    /// 
    /// RESPONSIBILITY: Governs all physical file retrieval (Download/View) and 
    /// enforces the multi-layered authorization cascade.
    /// </summary>
    public partial class DocumentsController
    {
        /// <summary>
        /// Orchestrates the 'Authorization Cascade' to determine if a user (guest or member) 
        /// can download a specific file version.
        /// </summary>
        [AllowAnonymous]
        public async Task<IActionResult> Download(int id, string? token = null, int? versionId = null)
        {
            // Privacy Standard: SuperAdmins are blocked from document content access
            if (User.IsInRole("SuperAdmin")) return Forbid();

            var doc = await _context.Documents.FindAsync(id);
            if (doc == null) return NotFound();

            bool isAuthorized = false;

            // Unified Link System: The token is now MANDATORY for all shared access.
            if (!string.IsNullOrEmpty(token))
            {
                if (doc.PublicShareToken != token) return NotFound();

                // If token is valid, check if it's public or restricted
                if (doc.PublicAccessLevel == "Viewer" || doc.PublicAccessLevel == "Editor")
                {
                    isAuthorized = true;
                }
            }

            // Check internal workspace permissions if not already authorized
            if (!isAuthorized)
            {
                if (CurrentWorkplace != null && CurrentMembership != null && doc.WorkplaceID == CurrentWorkplace.WorkplaceID)
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
            }

            if (!isAuthorized)
            {
                // REDIRECT: If access is restricted (or not authorized) and user is anonymous,
                // send them to Home with a clear prompt.
                if (User.Identity?.IsAuthenticated != true && !string.IsNullOrEmpty(token) && doc.PublicShareToken == token)
                {
                    TempData["InviteLoginPrompt"] = "This document is restricted. Please sign in to verify your access.";
                    return RedirectToAction("Index", "Home", new { ReturnUrl = Url.Action("Download", "Documents", new { id = id, token = token }) });
                }

                // If they have a valid token but it's restricted, challenge them to log in
                if (!string.IsNullOrEmpty(token) && doc.PublicShareToken == token) return Challenge();
                
                // Otherwise, simple 404
                return NotFound();
            }

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

            // CLOUDINARY SUPPORT: If the path is a full URL, use a signed URL for secure redirection.
            if (version.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var signedUrl = _cloudinaryService.GetSignedUrl(version.FilePath);
                return Redirect(signedUrl);
            }

            // Construct physical path for legacy local files
            var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var physicalPath = Path.Combine(webRoot, version.FilePath.Replace("/", "\\"));

            if (!System.IO.File.Exists(physicalPath)) return NotFound("Physical file missing.");

            var fileBytes = await System.IO.File.ReadAllBytesAsync(physicalPath);
            return File(fileBytes, version.MimeType ?? "application/octet-stream", version.FileName);
        }

        /// <summary>
        /// Similar to Download, but optimizes the response headers for in-browser 
        /// previewing (inline) rather than attachment.
        /// </summary>
        [AllowAnonymous]
        public async Task<IActionResult> ViewFile(int id, string? token = null, int? versionId = null)
        {
            // Privacy Standard: SuperAdmins are blocked from document content access
            if (User.IsInRole("SuperAdmin")) return Forbid();

            var doc = await _context.Documents.FindAsync(id);
            if (doc == null) return NotFound();

            bool isAuthorized = false;

            // Unified Link System: The token is now MANDATORY for all shared access.
            if (!string.IsNullOrEmpty(token))
            {
                if (doc.PublicShareToken != token) return NotFound();

                // If token is valid, check if it's public or restricted
                if (doc.PublicAccessLevel == "Viewer" || doc.PublicAccessLevel == "Editor")
                {
                    isAuthorized = true;
                }
            }

            // Check internal workspace permissions if not already authorized
            if (!isAuthorized)
            {
                if (CurrentWorkplace != null && CurrentMembership != null && doc.WorkplaceID == CurrentWorkplace.WorkplaceID)
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
            }

            if (!isAuthorized)
            {
                // REDIRECT: If access is restricted (or not authorized) and user is anonymous,
                // send them to Home with a clear prompt.
                if (User.Identity?.IsAuthenticated != true && !string.IsNullOrEmpty(token) && doc.PublicShareToken == token)
                {
                    TempData["InviteLoginPrompt"] = "This document is restricted. Please sign in to verify your access.";
                    return RedirectToAction("Index", "Home", new { ReturnUrl = Url.Action("ViewFile", "Documents", new { id = id, token = token }) });
                }

                // If they have a valid token but it's restricted, challenge them to log in
                if (!string.IsNullOrEmpty(token) && doc.PublicShareToken == token) return Challenge();
                
                // Otherwise, simple 404
                return NotFound();
            }

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

            // CLOUDINARY SUPPORT: Redirect for preview using a secure signed URL
            if (version.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var signedUrl = _cloudinaryService.GetSignedUrl(version.FilePath);
                return Redirect(signedUrl);
            }

            var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var physicalPath = Path.Combine(webRoot, version.FilePath.Replace("/", "\\"));

            if (!System.IO.File.Exists(physicalPath)) return NotFound("Physical file missing.");

            var fileBytes = await System.IO.File.ReadAllBytesAsync(physicalPath);
            
            var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(version.FileName ?? string.Empty, out var contentType))
            {
                contentType = version.MimeType ?? "application/octet-stream";
            }

            Response.Headers.Append("Content-Disposition", "inline; filename=\"" + version.FileName + "\"");
            return File(fileBytes, contentType);
        }
    }
}
