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

namespace FileMatrix_Pabiran_.Areas.Admin.Controllers
{
    [Area("Admin")]
    /// <summary>
    /// DocumentsController: The Central Hub for Workplace Asset Management.
    /// 
    /// RESPONSIBILITY: Orchestrates all document-related actions (Listing, Uploading, Sharing, Actions).
    /// Note: This controller is split into multiple partial files for maintainability.
    /// Core file operations are delegated to the <see cref="DocumentService"/>.
    /// </summary>
    public partial class DocumentsController : BaseAdminController
    {
        private readonly FileMatrix_Pabiran_.Services.DocumentService _documentService;
        private readonly FileMatrix_Pabiran_.Services.CloudinaryService _cloudinaryService;
        private readonly FileMatrix_Pabiran_.Services.EmailSenderService _emailSender;

        public DocumentsController(
            FileMatrix_Pabiran_.Data.ApplicationDbContext context, 
            FileMatrix_Pabiran_.Services.DocumentService documentService, 
            FileMatrix_Pabiran_.Services.CloudinaryService cloudinaryService,
            FileMatrix_Pabiran_.Services.EmailSenderService emailSender) : base(context)
        {
            _documentService = documentService;
            _cloudinaryService = cloudinaryService;
            _emailSender = emailSender;
        }

    public async Task<IActionResult> Index(string? query, int? categoryId, string? status)
    {
        try
        {
            if (CurrentWorkplace == null) return RedirectToAction("Index", "Organizations", new { area = "" });

            var docQuery = _context.Documents
                .Where(d => d.WorkplaceID == CurrentWorkplace.WorkplaceID);

            if (!string.IsNullOrEmpty(status))
            {
                docQuery = docQuery.Where(d => d.Status == status);
            }
            else
            {
                // Default view excludes archived
                docQuery = docQuery.Where(d => d.Status != "Archived");
            }

            if (!string.IsNullOrEmpty(query))
            {
                var lowerQuery = query.ToLower();
                docQuery = docQuery.Where(d => (d.Title != null && d.Title.ToLower().Contains(lowerQuery)) || (d.Description != null && d.Description.ToLower().Contains(lowerQuery)));
            }

            if (categoryId.HasValue)
            {
                docQuery = docQuery.Where(d => d.CategoryID == categoryId);
            }

            // Simple Query: Look up all documents that match our filters (category, search text, etc.)
            var documentsRaw = await docQuery
                .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
                .ToListAsync();

            var vm = new DocumentListViewModel
            {
                WorkplaceID = CurrentWorkplace.WorkplaceID,
                WorkplaceName = CurrentWorkplace.Name ?? "My Workspace",
                SearchQuery = query,
                CategoryID = categoryId,
                StatusFilter = status,
                Documents = new List<DocumentItemViewModel>()
            };

            // Simple Query: Get all the category names for our workplace so we can show them in the list
            var categories = await _context.Categories
                .Where(c => c.WorkplaceID == CurrentWorkplace.WorkplaceID)
                .ToDictionaryAsync(c => c.CategoryID, c => c.Name);

            foreach (var doc in documentsRaw)
            {
                // Simple Query: For each document, find its most recent version to get the file size and uploader
                var latestVersion = await _context.DocumentVersions
                    .Where(v => v.DocumentID == doc.DocumentID)
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefaultAsync();

                var uploader = await _context.Users.FindAsync(latestVersion?.UploadedByUserID);
                var uploaderName = uploader?.DisplayName ?? uploader?.Username ?? "Admin User";

                vm.Documents.Add(new DocumentItemViewModel
                {
                    DocumentID = doc.DocumentID,
                    CategoryID = doc.CategoryID,
                    Title = doc.Title ?? "Untitled",
                    Description = doc.Description,
                    CategoryName = doc.CategoryID != null && categories.ContainsKey(doc.CategoryID.Value) ? categories[doc.CategoryID.Value] ?? "Uncategorized" : "Uncategorized",
                    FileName = latestVersion?.FileName ?? "No file",
                    FileSizeFormatted = FormatBytes(latestVersion?.FileSizeBytes ?? 0),
                    CurrentVersionNumber = latestVersion?.VersionNumber.ToString("0.0") ?? "1.0",
                    UpdatedAt = doc.UpdatedAt ?? doc.CreatedAt,
                    GoogleDriveFileID = doc.GoogleDriveFileID,
                    GoogleDriveLink = doc.GoogleDriveLink,
                    UploadedBy = uploaderName,
                    Author = uploaderName,
                    IsFavorite = doc.IsFavorite,
                    Status = doc.Status ?? "Published",
                    MimeType = latestVersion?.MimeType,
                    PublicShareToken = doc.PublicShareToken,
                    PublicAccessLevel = doc.PublicAccessLevel,
                    Tags = new List<string> { (doc.CategoryID != null && categories.ContainsKey(doc.CategoryID.Value) && categories[doc.CategoryID.Value] != null ? categories[doc.CategoryID.Value]!.ToLower() : "general"), "report" },
                    IsShared = !string.IsNullOrEmpty(doc.PublicShareToken) && (doc.PublicAccessLevel == "Viewer" || doc.PublicAccessLevel == "Editor")
                });
            }

            if (!string.IsNullOrEmpty(status) && status != "All Status")
            {
                vm.Documents = vm.Documents.Where(d => d.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            ViewBag.CategoriesList = await _context.Categories
                .Where(c => c.WorkplaceID == CurrentWorkplace.WorkplaceID)
                .ToListAsync();

            ViewBag.RetentionPolicy = await _context.RetentionPolicies
                .FirstOrDefaultAsync(p => p.WorkplaceID == CurrentWorkplace.WorkplaceID);

            return View(vm);
        }
        catch (Exception ex)
        {
            // Even in error state, try to provide categories for the upload modal
            try {
                var currentWpId = CurrentWorkplace?.WorkplaceID;
                ViewBag.CategoriesList = await _context.Categories
                    .Where(c => c.WorkplaceID == currentWpId)
                    .ToListAsync();
            } catch { /* Ignore category load errors in fallback */ }

            return View(new DocumentListViewModel { 
                WorkplaceName = CurrentWorkplace?.Name ?? "Workspace",
                Documents = new List<DocumentItemViewModel>(),
                SearchQuery = "Error loading documents: " + ex.Message
            });
        }
    }

        [AllowAnonymous]
        public async Task<IActionResult> Details(int id, string? token = null)
        {
            // Privacy Standard: SuperAdmins are blocked from document content access
            if (User.IsInRole("SuperAdmin")) return Forbid();

            var doc = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentID == id);
            
            if (doc == null) return NotFound();

            bool isAuthorized = false;

            // Unified Link System: The token is now MANDATORY for all shared access.
            // If the token is missing or incorrect, we return NotFound (security by obscurity).
            if (!string.IsNullOrEmpty(token))
            {
                if (doc.PublicShareToken != token) return NotFound();

                // If token is valid, check if it's public or restricted
                if (doc.PublicAccessLevel == "Viewer" || doc.PublicAccessLevel == "Editor")
                {
                    isAuthorized = true;
                }
            }
            
            // Check internal workspace permissions if not already authorized by public link
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
                    return RedirectToAction("Index", "Home", new { ReturnUrl = Url.Action("Details", "Documents", new { id = id, token = token }) });
                }

                // If they have a valid token but it's restricted, challenge them to log in
                if (!string.IsNullOrEmpty(token) && doc.PublicShareToken == token) return Challenge();
                
                // Otherwise, simple 404
                return NotFound();
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
            string workplaceName = "Workspace";
            if (CurrentWorkplace != null && CurrentWorkplace.WorkplaceID == doc.WorkplaceID)
            {
                workplaceName = CurrentWorkplace.Name ?? "Workspace";
            }
            else
            {
                var wp = await _context.Workplaces.FindAsync(doc.WorkplaceID);
                workplaceName = wp?.Name ?? "Workspace";
            }

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
                    GoogleDriveFileID = doc.GoogleDriveFileID,
                    GoogleDriveLink = doc.GoogleDriveLink,
                    UploadedBy = uploaderName,
                    IsFavorite = doc.IsFavorite,
                    Status = doc.Status ?? "Published",
                    CategoryName = category?.Name ?? "Uncategorized",
                    MimeType = latestVersion?.MimeType,
                    PublicShareToken = doc.PublicShareToken,
                    PublicAccessLevel = doc.PublicAccessLevel,
                    IsShared = !string.IsNullOrEmpty(doc.PublicShareToken) && (doc.PublicAccessLevel == "Viewer" || doc.PublicAccessLevel == "Editor"),
                    SignedUrl = signedUrl
                }
            };

            var comments = await _context.DocumentComments
                .Where(c => c.DocumentID == id)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            foreach (var c in comments)
            {
                var user = await _context.Users.FindAsync(c.UserID);
                vm.Comments.Add(new DocumentCommentViewModel
                {
                    CommentID = c.CommentID,
                    UserDisplayName = user?.DisplayName ?? user?.Username ?? "Unknown",
                    Text = c.Text,
                    CreatedAt = c.CreatedAt
                });
            }

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
