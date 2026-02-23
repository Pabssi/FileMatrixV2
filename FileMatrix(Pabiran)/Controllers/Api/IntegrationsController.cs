using FileMatrix_Pabiran_.Data;
using FileMatrix_Pabiran_.Models;
using FileMatrix_Pabiran_.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FileMatrix_Pabiran_.Controllers.Api
{
    [ApiController]
    [Route("api/[controller]")]
    public class IntegrationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly DocumentService _documentService;

        public IntegrationsController(ApplicationDbContext context, DocumentService documentService)
        {
            _context = context;
            _documentService = documentService;
        }

        /// <summary>
        /// Upload a document from an external system (e.g. POS)
        /// Header: X-FileMatrix-Key: {IntegrationApiKey}
        /// Body: multipart/form-data (file, title, description, entityType, entityId, externalRefId)
        /// </summary>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadDocument(
            [FromHeader(Name = "X-FileMatrix-Key")] string? apiKey,
            IFormFile file,
            [FromForm] string title,
            [FromForm] string? description,
            [FromForm] string? entityType,
            [FromForm] int? entityId,
            [FromForm] string? externalRefId,
            [FromForm] int? categoryId,
            [FromForm] int? folderId)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                return Unauthorized(new { message = "Integration API Key is missing." });
            }

            // 1. Validate API Key and Find Workplace
            var workplace = await _context.Workplaces
                .FirstOrDefaultAsync(w => w.IntegrationApiKey == apiKey && w.IsActive);

            if (workplace == null)
            {
                return Unauthorized(new { message = "Invalid or inactive Integration API Key." });
            }

            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "No file uploaded." });
            }

            try
            {
                // 2. Identify the system user for internal logging (find an admin of the workplace)
                var adminMember = await _context.WorkplaceMembers
                    .Where(m => m.WorkplaceID == workplace.WorkplaceID && m.RoleID == 1) // 1 = Admin
                    .FirstOrDefaultAsync();

                int userId = adminMember?.UserID ?? workplace.CreatedByUserID ?? 0;

                // 3. Upload using DocumentService
                var document = await _documentService.UploadDocumentAsync(
                    file, 
                    title, 
                    description, 
                    workplace.WorkplaceID, 
                    userId, 
                    folderId, 
                    categoryId);

                // 4. Update with External/Business metadata
                document.BusinessEntityType = entityType;
                document.BusinessEntityID = entityId;
                document.ExternalRefID = externalRefId;
                
                // If it's a "System" upload and no category specified, try to find/create a "System" category
                if (categoryId == null)
                {
                    var systemCategory = await _context.Categories
                        .FirstOrDefaultAsync(c => c.WorkplaceID == workplace.WorkplaceID && c.Name == "System Upload");
                    
                    if (systemCategory == null)
                    {
                        systemCategory = new Category
                        {
                            WorkplaceID = workplace.WorkplaceID,
                            Name = "System Upload",
                            Icon = "🤖",
                            Color = "#6366f1",
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.Categories.Add(systemCategory);
                        await _context.SaveChangesAsync();
                    }
                    document.CategoryID = systemCategory.CategoryID;
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    documentId = document.DocumentID,
                    message = "Document successfully ingested via automation API."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Internal error during document ingestion.", details = ex.Message });
            }
        }
    }
}
