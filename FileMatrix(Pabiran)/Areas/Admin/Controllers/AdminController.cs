using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using FileMatrix_Pabiran_.Areas.Admin.Models;
using FileMatrix_Pabiran_.Models;
using System.Collections.Generic;

namespace FileMatrix_Pabiran_.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Route("Admin")]
    /// <summary>
    /// AdminController: The Workplace Dashboard Hub.
    /// 
    /// RESPONSIBILITY: Aggregates high-level metrics (Storage, Members, Recent activity) 
    /// for the current workplace and presents them to the administrator.
    /// </summary>
    public class AdminController : BaseAdminController
    {
        public AdminController(FileMatrix_Pabiran_.Data.ApplicationDbContext context) : base(context)
        {
        }

        /// <summary>
        /// Compiles the dashboard view model by aggregating document stats 
        /// and resolving the latest version metadata for recent items.
        /// </summary>
        [HttpGet("Dashboard")]
        public async Task<IActionResult> Index()
        {
            try 
            {
                if (CurrentWorkplace == null) return RedirectToAction("Index", "Organizations", new { area = "" });

                var totalDocs = await _context.Documents.CountAsync(d => d.WorkplaceID == CurrentWorkplace.WorkplaceID);
                var totalMembers = await _context.WorkplaceMembers.CountAsync(m => m.WorkplaceID == CurrentWorkplace.WorkplaceID);
                
                var totalSizeBytes = await _context.Documents
                    .Where(d => d.WorkplaceID == CurrentWorkplace.WorkplaceID)
                    .Join(_context.DocumentVersions, d => d.DocumentID, dv => dv.DocumentID, (d, dv) => dv.FileSizeBytes)
                    .SumAsync(s => (long?)s) ?? 0;

                var recentDocsRaw = await _context.Documents
                    .Where(d => d.WorkplaceID == CurrentWorkplace.WorkplaceID)
                    .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
                    .Take(5)
                    .ToListAsync();

                var recentDocs = new List<DocumentItemViewModel>();
                foreach (var doc in recentDocsRaw)
                {
                    var latestVersion = await _context.DocumentVersions
                        .Where(v => v.DocumentID == doc.DocumentID)
                        .OrderByDescending(v => v.VersionNumber)
                        .FirstOrDefaultAsync();

                    recentDocs.Add(new DocumentItemViewModel
                    {
                        DocumentID = doc.DocumentID,
                        CategoryID = doc.CategoryID,
                        Title = doc.Title ?? "Untitled",
                        Description = doc.Description,
                        CategoryName = doc.Category?.Name ?? "Uncategorized",
                        FileName = latestVersion?.FileName ?? "No file",
                        FileSizeFormatted = FormatBytes(latestVersion?.FileSizeBytes ?? 0),
                        CurrentVersionNumber = latestVersion?.VersionNumber.ToString("0.0") ?? "1.0",
                        UpdatedAt = doc.UpdatedAt ?? doc.CreatedAt,
                        IsFavorite = doc.IsFavorite,
                        Status = doc.Status ?? "Published",
                        MimeType = latestVersion?.MimeType,
                        PublicShareToken = doc.PublicShareToken,
                    });
                }

                var vm = new AdminViewModel 
                { 
                    Message = "Admin Dashboard",
                    TotalDocuments = totalDocs,
                    TotalMembers = totalMembers,
                    TotalSizeBytes = totalSizeBytes,
                    TotalSizeFormatted = FormatBytes(totalSizeBytes),
                    WorkplaceName = CurrentWorkplace.Name ?? "My Workspace",
                    RecentDocuments = recentDocs
                };

                return View(vm);
            }
            catch (System.Exception ex)
            {
                // Fallback for unexpected data errors to keep the dashboard accessible
                return View(new AdminViewModel { 
                    Message = "Dashboard loaded with errors: " + ex.Message,
                    WorkplaceName = CurrentWorkplace?.Name ?? "Workspace" 
                });
            }
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
