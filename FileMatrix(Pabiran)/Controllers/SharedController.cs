using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FileMatrix_Pabiran_.Data;
using FileMatrix_Pabiran_.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace FileMatrix_Pabiran_.Controllers
{
    [AllowAnonymous]
    public class SharedController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SharedController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Document(int id, string? token = null)
        {
            var doc = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentID == id);
            
            if (doc == null) return NotFound();

            bool isAuthorized = false;

            // 1. Check if public link token is valid AND public access is enabled
            if (!string.IsNullOrEmpty(token) && doc.PublicShareToken == token)
            {
                if (doc.PublicAccessLevel != null)
                {
                    isAuthorized = true;
                }
            }

            // 2. Check if user is authenticated and has explicit permission
            if (!isAuthorized && User.Identity?.IsAuthenticated == true)
            {
                var email = User.FindFirstValue(ClaimTypes.Email);
                if (!string.IsNullOrEmpty(email))
                {
                    var userId = await _context.Users.Where(u => u.Email == email).Select(u => u.UserID).FirstOrDefaultAsync();
                    if (userId > 0)
                    {
                        // Auto-claim any pending email invitations
                        var pendingInvite = await _context.DocumentShareInvitations
                            .FirstOrDefaultAsync(i => i.DocumentID == id && i.Email == email && !i.IsAccepted);

                        if (pendingInvite != null)
                        {
                            var existing = await _context.DocumentPermissions
                                .FirstOrDefaultAsync(p => p.DocumentID == id && p.UserID == userId);
                            if (existing == null)
                            {
                                _context.DocumentPermissions.Add(new DocumentPermission
                                {
                                    DocumentID = id,
                                    UserID = userId,
                                    PermissionLevel = pendingInvite.PermissionLevel,
                                    RoleName = "User"
                                });
                            }
                            pendingInvite.IsAccepted = true;
                            pendingInvite.AcceptedAt = DateTime.UtcNow;
                            await _context.SaveChangesAsync();
                            isAuthorized = true;
                        }
                        else
                        {
                            isAuthorized = await _context.DocumentPermissions.AnyAsync(p => p.DocumentID == id && p.UserID == userId);
                            
                            // Fallback: if they are a member of the workplace that owns this document
                            if (!isAuthorized)
                            {
                                var isMember = await _context.WorkplaceMembers.AnyAsync(m => m.WorkplaceID == doc.WorkplaceID && m.UserID == userId);
                                if (isMember) isAuthorized = true;
                            }
                        }
                    }
                }
            }

            if (!isAuthorized) 
            {
                // If not authorized and not logged in, redirect to Home with a return URL and a login prompt
                if (User.Identity?.IsAuthenticated != true)
                {
                    TempData["InviteLoginPrompt"] = "Please sign in to access your shared document.";
                    return RedirectToAction("Index", "Home", new { ReturnUrl = Url.Action("Document", "Shared", new { id = id, token = token }) });
                }
                return Forbid();
            }

            var latestVersion = await _context.DocumentVersions
                .Where(v => v.DocumentID == doc.DocumentID)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync();

            var uploaderName = "System";
            if (latestVersion?.UploadedByUserID != null)
            {
                var uploader = await _context.Users.FindAsync(latestVersion.UploadedByUserID);
                uploaderName = uploader?.DisplayName ?? uploader?.Username ?? "System";
            }

            var category = await _context.Categories.FindAsync(doc.CategoryID);

            // Fetch workplace name
            var wp = await _context.Workplaces.FindAsync(doc.WorkplaceID);
            string workplaceName = wp?.Name ?? "Workspace";

            var vm = new DocumentDetailsViewModel
            {
                WorkplaceID = doc.WorkplaceID,
                WorkplaceName = workplaceName,
                Document = new DocumentItemViewModel
                {
                    DocumentID = doc.DocumentID,
                    CategoryID = doc.CategoryID,
                    Title = doc.Title ?? "Untitled",
                    Description = doc.Description,
                    FileName = latestVersion?.FileName ?? "No file",
                    FileSizeFormatted = FormatBytes(latestVersion?.FileSizeBytes ?? 0),
                    CurrentVersionNumber = latestVersion?.VersionNumber.ToString("0.0") ?? "1.0",
                    UpdatedAt = doc.UpdatedAt ?? doc.CreatedAt,
                    UploadedBy = uploaderName,
                    IsFavorite = doc.IsFavorite,
                    Status = doc.Status ?? "Published",
                    CategoryName = category?.Name ?? "Uncategorized",
                    MimeType = latestVersion?.MimeType,
                    PublicShareToken = doc.PublicShareToken,
                    IsShared = !string.IsNullOrEmpty(doc.PublicShareToken)
                }
            };

            // Set layout variable to hide main nav
            ViewData["HideMainNav"] = true;
            ViewData["Title"] = doc.Title;

            return View(vm);
        }

        private string FormatBytes(long bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }
            return $"{dblSByte:0.##} {Suffix[i]}";
        }
    }
}
