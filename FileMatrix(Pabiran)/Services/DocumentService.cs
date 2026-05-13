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
    /// <summary>
    /// DocumentService: The File System & Versioning Engine.
    /// 
    /// STRATEGY: Physical-First Storage with Database Tracking.
    /// 1. Storage: Files are stored in 'wwwroot/uploads/{WorkplaceID}/' using GUIDs for collision prevention.
    /// 2. Versioning: Every change creates a NEW record in the DocumentVersions table.
    /// 3. Immutability: Standard files are never overwritten; only new versions are appended.
    /// </summary>
    public class DocumentService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly CloudinaryService _cloudinary;
        private readonly GoogleDriveService _googleDrive;

        public DocumentService(ApplicationDbContext context, IWebHostEnvironment environment, CloudinaryService cloudinary, GoogleDriveService googleDrive)
        {
            _context = context;
            _environment = environment;
            _cloudinary = cloudinary;
            _googleDrive = googleDrive;
        }

        /// <summary>
        /// Orchestrates the 'Initial Ingestion': Saves the physical file under 
        /// the workplace directory, creates the base Document record, and initializes 
        /// the first immutable version (v1.0).
        /// </summary>
        public async Task<Document> UploadDocumentAsync(IFormFile file, string title, string? description, int workplaceId, int userId, int? folderId = null, int? categoryId = null)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty");

            // 1. Upload to Cloudinary instead of local disk
            CloudinaryUploadResult uploadResult;
            using (var stream = file.OpenReadStream())
            {
                uploadResult = await _cloudinary.UploadAsync(stream, file.FileName, workplaceId.ToString());
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

            // Simple Query: Add the new document record to the database and save it to get its unique ID.
            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            // 4.5 Backup to Google Drive (Backpack)
            var workplace = await _context.Workplaces.FindAsync(workplaceId);
            if (workplace != null && !string.IsNullOrEmpty(workplace.GoogleDriveRefreshToken))
            {
                try
                {
                    using (var backupStream = file.OpenReadStream())
                    {
                        var backupResult = await _googleDrive.UploadFileAsync(workplace, file.FileName, file.ContentType, backupStream);
                        if (!string.IsNullOrEmpty(backupResult.FileId))
                        {
                            document.GoogleDriveFileID = backupResult.FileId;
                            document.GoogleDriveLink = backupResult.WebViewLink;
                            await _context.SaveChangesAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log error but don't fail the whole upload
                    System.Diagnostics.Debug.WriteLine($"Google Drive Backup Failed: {ex.Message}");
                }
            }

            // 5. Create DocumentVersion record
            var version = new DocumentVersion
            {
                DocumentID = document.DocumentID,
                VersionNumber = 1.0m,
                FileName = file.FileName,
                FilePath = uploadResult.SecureUrl, // Store secure Cloudinary URL
                ExternalPublicID = uploadResult.PublicId,
                FileSizeBytes = file.Length,
                MimeType = file.ContentType,
                UploadedByUserID = userId,
                UploadedAt = DateTime.UtcNow,
                ChangeNote = "Initial upload"
            };

            _context.DocumentVersions.Add(version);
            await _context.SaveChangesAsync();

            // Simple Query: Link the main document record to its newly created version.
            document.CurrentVersionID = version.VersionID;
            await _context.SaveChangesAsync();

            // 6. Log activity (Resilient)
            try
            {
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
            }
            catch (Exception ex)
            {
                // Safety net: Log failed audit entries but don't crash the upload
                System.Diagnostics.Debug.WriteLine($"Audit Log failed during upload: {ex.Message}");
            }

            return document;
        }

        /// <summary>
        /// Orchestrates the 'Version Append': Saves a new physical file and 
        /// pushes a new record to the version history timeline without 
        /// affecting existing versions.
        /// </summary>
        public async Task<DocumentVersion> UploadVersionAsync(int documentId, IFormFile file, string changeNote, int userId)
        {
            // Simple Query: Find the document we are trying to add a new version to.
            var doc = await _context.Documents.FindAsync(documentId);
            if (doc == null) throw new ArgumentException("Document not found");

            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty");

            // 1. Upload to Cloudinary
            CloudinaryUploadResult uploadResult;
            using (var stream = file.OpenReadStream())
            {
                uploadResult = await _cloudinary.UploadAsync(stream, file.FileName, doc.WorkplaceID.ToString());
            }

            // 4. Determine next version number
            // Simple Query: Find the currently active version number so we can figure out the next one (e.g., 2.0).
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
                FilePath = uploadResult.SecureUrl, // Store secure Cloudinary URL
                ExternalPublicID = uploadResult.PublicId,
                FileSizeBytes = file.Length,
                MimeType = file.ContentType,
                UploadedByUserID = userId,
                UploadedAt = DateTime.UtcNow,
                ChangeNote = changeNote
            };

            _context.DocumentVersions.Add(version);
            await _context.SaveChangesAsync();

            // 6. Update Document and Backup to Google Drive
            doc.CurrentVersionID = version.VersionID;
            doc.UpdatedAt = DateTime.UtcNow;

            var workplace = await _context.Workplaces.FindAsync(doc.WorkplaceID);
            if (workplace != null && !string.IsNullOrEmpty(workplace.GoogleDriveRefreshToken))
            {
                try
                {
                    using (var backupStream = file.OpenReadStream())
                    {
                        var backupResult = await _googleDrive.UploadFileAsync(workplace, file.FileName, file.ContentType, backupStream);
                        if (!string.IsNullOrEmpty(backupResult.FileId))
                        {
                            doc.GoogleDriveFileID = backupResult.FileId;
                            doc.GoogleDriveLink = backupResult.WebViewLink;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Google Drive Backup Failed: {ex.Message}");
                }
            }

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
