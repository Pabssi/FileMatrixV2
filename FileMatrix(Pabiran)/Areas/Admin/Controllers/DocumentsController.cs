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
    public partial class DocumentsController : BaseAdminController
    {
        private readonly FileMatrix_Pabiran_.Services.DocumentService _documentService;
        private readonly FileMatrix_Pabiran_.Services.EmailSenderService _emailSender;

        public DocumentsController(FileMatrix_Pabiran_.Data.ApplicationDbContext context, FileMatrix_Pabiran_.Services.DocumentService documentService, FileMatrix_Pabiran_.Services.EmailSenderService emailSender) : base(context)
        {
            _documentService = documentService;
            _emailSender = emailSender;
        }

        public async Task<IActionResult> Index(string? query, int? categoryId, string? status)
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
                docQuery = docQuery.Where(d => d.Title.ToLower().Contains(lowerQuery) || (d.Description != null && d.Description.ToLower().Contains(lowerQuery)));
            }

            if (categoryId.HasValue)
            {
                docQuery = docQuery.Where(d => d.CategoryID == categoryId);
            }

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

            var categories = await _context.Categories
                .Where(c => c.WorkplaceID == CurrentWorkplace.WorkplaceID)
                .ToDictionaryAsync(c => c.CategoryID, c => c.Name);

            foreach (var doc in documentsRaw)
            {
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
                    CategoryName = doc.CategoryID != null && categories.ContainsKey(doc.CategoryID.Value) ? categories[doc.CategoryID.Value] : "Uncategorized",
                    FileName = latestVersion?.FileName ?? "No file",
                    FileSizeFormatted = FormatBytes(latestVersion?.FileSizeBytes ?? 0),
                    CurrentVersionNumber = latestVersion?.VersionNumber.ToString("0.0") ?? "1.0",
                    UpdatedAt = doc.UpdatedAt ?? doc.CreatedAt,
                    UploadedBy = uploaderName,
                    Author = uploaderName,
                    IsFavorite = doc.IsFavorite,
                    Status = doc.Status ?? "Published",
                    MimeType = latestVersion?.MimeType,
                    PublicShareToken = doc.PublicShareToken,
                    Tags = new List<string> { (doc.CategoryID != null && categories.ContainsKey(doc.CategoryID.Value) ? categories[doc.CategoryID.Value].ToLower() : "general"), "report" },
                    IsShared = !string.IsNullOrEmpty(doc.PublicShareToken)
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

        [AllowAnonymous]
        public async Task<IActionResult> Details(int id, string? token = null)
        {
            // Privacy Standard: SuperAdmins are blocked from document content access
            if (User.IsInRole("SuperAdmin")) return Forbid();

            var doc = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentID == id);
            
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
