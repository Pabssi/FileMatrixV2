using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FileMatrix_Pabiran_.Data;
using FileMatrix_Pabiran_.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace FileMatrix_Pabiran_.Controllers
{
    /// <summary>
    /// SharedController: The External & Guest Access Gateway.
    /// 
    /// LOGIC: The "Authorization Cascade".
    /// Access follows this priority:
    /// 1. Public Link Token (If access is Viewer/Editor).
    /// 2. Individual Permissions (Direct user-to-document grants).
    /// 3. Workplace Membership (Internal team access).
    /// </summary>
    [AllowAnonymous]
    public class SharedController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly FileMatrix_Pabiran_.Services.CloudinaryService _cloudinaryService;

        public SharedController(ApplicationDbContext context, FileMatrix_Pabiran_.Services.CloudinaryService cloudinaryService)
        {
            _context = context;
            _cloudinaryService = cloudinaryService;
        }

        /// <summary>
        /// The 'Document Gateway': Enforces the Authorization Cascade to determine 
        /// if the requester (Guest, Authenticated Guest, or Team Member) can view 
        /// the document metadata.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Document(int id, string? token = null)
        {
            var doc = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentID == id);
            
            if (doc == null) return NotFound();

            bool isAuthorized = false;

            // Unified Link System: The token is now MANDATORY for all shared access.
            // If the token is missing or incorrect, we return NotFound (security by obscurity).
            // The token is always required for public access, even if the level is Viewer/Editor.
            if (string.IsNullOrEmpty(token) || doc.PublicShareToken != token)
            {
                return NotFound();
            }

            // 1. If public access is enabled (Viewer/Editor), and the token is correct, grant immediate access
            if (doc.PublicAccessLevel == "Viewer" || doc.PublicAccessLevel == "Editor")
            {
                isAuthorized = true;
            }

            // 2. If access is NOT authorized yet, check for authenticated user permissions
            if (!isAuthorized && User.Identity?.IsAuthenticated == true)
            {
                var email = User.FindFirstValue(ClaimTypes.Email);
                if (!string.IsNullOrEmpty(email))
                {
                    var userId = await _context.Users.Where(u => u.Email == email).Select(u => u.UserID).FirstOrDefaultAsync();
                    if (userId > 0)
                    {
                        // RE-CLAIM: The 'Invite-to-Permission' bridge. 
                        // If the user lands here via a token but has a pending 
                        // individual invite, we materialize that into a permanent Permission.
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
                            // Check direct document permissions
                            isAuthorized = await _context.DocumentPermissions.AnyAsync(p => p.DocumentID == id && p.UserID == userId);
                            
                            // Fallback: if they are a member of the workplace that owns this document
                            if (!isAuthorized)
                            {
                                isAuthorized = await _context.WorkplaceMembers.AnyAsync(m => m.WorkplaceID == doc.WorkplaceID && m.UserID == userId);
                            }
                        }
                    }
                }
            }

            if (!isAuthorized) 
            {
                // REDIRECT: If access is restricted (or not authorized) and user is anonymous,
                // send them to Home with a clear prompt.
                if (User.Identity?.IsAuthenticated != true)
                {
                    // SMART REDIRECT: Redirecting to the landing page with a return URL 
                    // ensures guests are prompted to log in without losing their 
                    // intended destination.
                    TempData["InviteLoginPrompt"] = "This document is restricted. Please sign in to verify your access.";
                    return RedirectToAction("Index", "Home", new { ReturnUrl = Url.Action("Document", "Shared", new { id = id, token = token }) });
                }
                
                // FORBID: If they are logged in but still don't have access
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

            // PRE-SIGN Cloudinary URL for Office Viewer
            string? signedUrl = null;
            if (latestVersion?.FilePath != null && latestVersion.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                signedUrl = _cloudinaryService.GetSignedUrl(latestVersion.FilePath);
            }

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
                    IsShared = !string.IsNullOrEmpty(doc.PublicShareToken),
                    SignedUrl = signedUrl
                }
            };

            // Set layout variable to hide main nav
            ViewData["HideMainNav"] = true;
            ViewData["Title"] = doc.Title;

            return View(vm);
        }

        private string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] Suffix = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            int i = 0;
            double dblSByte = bytes;
            while (dblSByte >= 1024 && i < Suffix.Length - 1)
            {
                dblSByte /= 1024.0;
                i++;
            }
            return $"{dblSByte:0.##} {Suffix[i]}";
        }
    }
}
