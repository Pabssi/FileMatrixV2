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
    /// DocumentsController.Comments: The Document Feedback Loop.
    /// 
    /// RESPONSIBILITY: Manages user social interactions (Comments) on documents.
    /// COMMENTS: Unlike standard assets, comments are linked to the global UserID 
    /// but contextually displayed within the workplace document view.
    /// </summary>
    public partial class DocumentsController
    {
        /// <summary>
        /// Adds a user comment to a specific document and returns the JSON-serialized comment data 
        /// for immediate UI update.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> AddComment(int documentId, string text)
        {
            if (!User.Identity?.IsAuthenticated ?? false) return Challenge();

            if (string.IsNullOrWhiteSpace(text)) return Json(new { success = false, message = "Comment cannot be empty" });

            var doc = await _context.Documents.FindAsync(documentId);
            if (doc == null) return NotFound();

            var email = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(email)) return Challenge();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null) return Unauthorized("User profile not found.");

            var comment = new DocumentComment
            {
                DocumentID = documentId,
                UserID = user.UserID,
                Text = text,
                CreatedAt = DateTime.UtcNow
            };

            _context.DocumentComments.Add(comment);
            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                comment = new {
                    commentID = comment.CommentID,
                    userDisplayName = user?.DisplayName ?? user?.Username ?? "You",
                    text = comment.Text,
                    createdAt = comment.CreatedAt.ToString("MMM dd, yyyy HH:mm")
                }
            });
        }
    }
}
