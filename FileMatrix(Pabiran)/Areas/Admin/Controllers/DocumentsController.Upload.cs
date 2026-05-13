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
    /// DocumentsController.Upload: Content Ingestion & Organization.
    /// 
    /// RESPONSIBILITY: Handles the initial creation of document assets and 
    /// hierarchical folders. 
    /// DELEGATION: File persistence is handled by the <see cref="DocumentService"/>.
    /// </summary>
    public partial class DocumentsController
    {
        /// <summary>
        /// Entry point for new document uploads. Enforces RBAC and delegates 
        /// to DocumentService for physical storage.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(104857600)] // 100MB Limit
        public async Task<IActionResult> Upload(string title, string? description, int? categoryId, IFormFile file)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();

            // RBAC Check: Only Admin(1) and Editor(2) can upload
            if (CurrentMembership.RoleID > 2)
            {
                TempData["ErrorMessage"] = "You do not have permission to upload documents.";
                return RedirectToAction(nameof(Index));
            }

            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "Please select a valid file to upload.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var userId = CurrentMembership.UserID;
                await _documentService.UploadDocumentAsync(file, title, description, CurrentWorkplace.WorkplaceID, userId, null, categoryId);
                TempData["SuccessMessage"] = $"Document '{title}' uploaded successfully.";
            }
            catch (Exception ex)
            {
                var detailedError = ex.Message;
                if (ex.InnerException != null)
                {
                    detailedError += " -> " + ex.InnerException.Message;
                    if (ex.InnerException.InnerException != null)
                    {
                        detailedError += " -> " + ex.InnerException.InnerException.Message;
                    }
                }
                TempData["ErrorMessage"] = $"Upload failed: {detailedError}";
            }

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Creates a new virtual folder within the workplace hierarchy.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFolder(string name, int? parentFolderId)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 2) return Forbid(); // Admin(1) or Editor(2) only

            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["ErrorMessage"] = "Folder name cannot be empty.";
                return RedirectToAction(nameof(Index), new { folderId = parentFolderId });
            }

            var folder = new Folder
            {
                WorkplaceID = CurrentWorkplace.WorkplaceID,
                Name = name.Trim(),
                ParentFolderID = parentFolderId,
                CreatedByUserID = CurrentMembership.UserID,
                CreatedAt = DateTime.UtcNow
            };

            _context.Folders.Add(folder);
            await _context.SaveChangesAsync();

            // Log activity
            var log = new AuditLog
            {
                WorkplaceID = CurrentWorkplace.WorkplaceID,
                Action = "Folder Created",
                EntityType = "Folder",
                EntityID = folder.FolderID,
                UserID = CurrentMembership.UserID,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                PerformedAt = DateTime.UtcNow,
                Details = $"Created folder: {folder.Name}"
            };
            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Folder created successfully.";
            return RedirectToAction(nameof(Index), new { folderId = parentFolderId });
        }
    }
}
