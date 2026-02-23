using FileMatrix_Pabiran_.Data;
using FileMatrix_Pabiran_.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FileMatrix_Pabiran_.Services
{
    public class DocumentService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public DocumentService(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        public async Task<Document> UploadDocumentAsync(IFormFile file, string title, string? description, int workplaceId, int userId, int? folderId = null, int? categoryId = null)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty");

            // 1. Ensure upload directory exists
            var uploadsRoot = Path.Combine(_environment.WebRootPath, "uploads", workplaceId.ToString());
            if (!Directory.Exists(uploadsRoot))
            {
                Directory.CreateDirectory(uploadsRoot);
            }

            // 2. Generate unique filename to avoid collisions
            var fileExtension = Path.GetExtension(file.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
            var filePath = Path.Combine(uploadsRoot, uniqueFileName);

            // 3. Save physical file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // 4. Create Document record
            var document = new Document
            {
                WorkplaceID = workplaceId,
                FolderID = folderId,
                Title = title,
                Description = description,
                CategoryID = categoryId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedByUserID = userId
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            // 5. Create DocumentVersion record
            var version = new DocumentVersion
            {
                DocumentID = document.DocumentID,
                VersionNumber = 1.0m,
                FileName = file.FileName,
                FilePath = Path.Combine("uploads", workplaceId.ToString(), uniqueFileName).Replace("\\", "/"),
                FileSizeBytes = file.Length,
                MimeType = file.ContentType,
                UploadedByUserID = userId,
                UploadedAt = DateTime.UtcNow,
                ChangeNote = "Initial upload"
            };

            _context.DocumentVersions.Add(version);
            await _context.SaveChangesAsync();

            // Update document with current version ID
            document.CurrentVersionID = version.VersionID;
            await _context.SaveChangesAsync();

            // 6. Log activity
            var log = new AuditLog
            {
                WorkplaceID = workplaceId,
                Action = "Document Uploaded",
                EntityType = "Document",
                EntityID = document.DocumentID,
                UserID = userId,
                PerformedAt = DateTime.UtcNow,
                Details = $"Uploaded file: {file.FileName} (v1.0)"
            };
            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();

            return document;
        }

        public async Task<DocumentVersion> UploadVersionAsync(int documentId, IFormFile file, string changeNote, int userId)
        {
            var doc = await _context.Documents.FindAsync(documentId);
            if (doc == null) throw new ArgumentException("Document not found");

            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty");

            // 1. Ensure upload directory exists
            var uploadsRoot = Path.Combine(_environment.WebRootPath, "uploads", doc.WorkplaceID.ToString());
            if (!Directory.Exists(uploadsRoot))
            {
                Directory.CreateDirectory(uploadsRoot);
            }

            // 2. Generate unique filename
            var fileExtension = Path.GetExtension(file.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
            var filePath = Path.Combine(uploadsRoot, uniqueFileName);

            // 3. Save physical file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // 4. Determine next version number
            var currentLatest = await _context.DocumentVersions
                .Where(v => v.DocumentID == documentId)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync();

            decimal nextVersion = (currentLatest?.VersionNumber ?? 1.0m) + 1.0m;

            // 5. Create Version
            var version = new DocumentVersion
            {
                DocumentID = documentId,
                VersionNumber = nextVersion,
                FileName = file.FileName,
                FilePath = Path.Combine("uploads", doc.WorkplaceID.ToString(), uniqueFileName).Replace("\\", "/"),
                FileSizeBytes = file.Length,
                MimeType = file.ContentType,
                UploadedByUserID = userId,
                UploadedAt = DateTime.UtcNow,
                ChangeNote = changeNote
            };

            _context.DocumentVersions.Add(version);
            await _context.SaveChangesAsync();

            // 6. Update Document
            doc.CurrentVersionID = version.VersionID;
            doc.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // 7. Log activity
            var log = new AuditLog
            {
                WorkplaceID = doc.WorkplaceID,
                Action = "New Version Uploaded",
                EntityType = "Document",
                EntityID = doc.DocumentID,
                UserID = userId,
                PerformedAt = DateTime.UtcNow,
                Details = $"Uploaded new version: {file.FileName} (v{nextVersion:0.0})"
            };
            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();

            return version;
        }
    }
}
