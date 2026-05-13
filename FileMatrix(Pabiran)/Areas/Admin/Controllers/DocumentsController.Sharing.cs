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
    /// DocumentsController.Sharing: The Collaborative Access Hub.
    /// 
    /// RESPONSIBILITY: Manages both internal team permissions and external guest sharing.
    /// DESIGN: Implements a "Unified Link System" where a single token can represent 
    /// public access or a restricted invite-only landing page.
    /// </summary>
    public partial class DocumentsController
    {
        /// <summary>
        /// Fetches the consolidated sharing state (Permissions, Links, Pending Invites) 
        /// to populate the sharing modal.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetShareStatus(int id)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();

            var doc = await _context.Documents.FindAsync(id);
            if (doc == null || doc.WorkplaceID != CurrentWorkplace.WorkplaceID)
                return Json(new { success = false, message = "Document not found." });

            // Simple Query: Find all people who have been given special access to this specific file.
            var permissions = await _context.DocumentPermissions
                .Where(p => p.DocumentID == id)
                .Select(p => new
                {
                    permissionID = p.PermissionID,
                    userID = p.UserID,
                    userEmail = _context.Users.Where(u => u.UserID == p.UserID).Select(u => u.Email).FirstOrDefault(),
                    userDisplayName = _context.Users.Where(u => u.UserID == p.UserID).Select(u => u.DisplayName ?? u.Username).FirstOrDefault(),
                    role = p.PermissionLevel, // Viewer, Editor
                    avatarUrl = "" // Placeholder
                })
                .ToListAsync();

            // Ensure a token exists for the unified link system
            if (string.IsNullOrEmpty(doc.PublicShareToken))
            {
                doc.PublicShareToken = Guid.NewGuid().ToString("n");
                // Secure by default: new shares are Restricted (Invite Only)
                doc.PublicAccessLevel = "Restricted"; 
                await _context.SaveChangesAsync();
            }
            else if (string.IsNullOrEmpty(doc.PublicAccessLevel))
            {
                // Fix for existing documents with token but no level
                doc.PublicAccessLevel = "Restricted";
                await _context.SaveChangesAsync();
            }

            var publicLink = Url.Action("Document", "Shared", new { area = "", id = id, token = doc.PublicShareToken }, HttpContext.Request.Scheme);

            var owner = await _context.Users
                .Where(u => u.UserID == doc.CreatedByUserID)
                .Select(u => new { 
                    userID = u.UserID, 
                    userDisplayName = u.DisplayName ?? u.Username, 
                    email = u.Email 
                })
                .FirstOrDefaultAsync();

            var pendingInvites = await _context.DocumentShareInvitations
                .Where(i => i.DocumentID == id && !i.IsAccepted)
                .Select(i => new
                {
                    email = i.Email,
                    role = i.PermissionLevel,
                    isPending = true
                })
                .ToListAsync();

            return Json(new
            {
                success = true,
                hasToken = !string.IsNullOrEmpty(doc.PublicShareToken),
                isPublic = doc.PublicAccessLevel == "Viewer" || doc.PublicAccessLevel == "Editor",
                publicAccessLevel = doc.PublicAccessLevel ?? "Restricted",
                publicLink = publicLink,
                permissions = permissions,
                pendingInvites = pendingInvites,
                owner = owner
            });
        }

        [HttpGet]
        public async Task<IActionResult> Share(int id)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 2) return Forbid();

            var doc = await _context.Documents.FindAsync(id);
            if (doc == null || doc.WorkplaceID != CurrentWorkplace.WorkplaceID) return NotFound();

            if (string.IsNullOrEmpty(doc.PublicShareToken))
            {
                doc.PublicShareToken = Guid.NewGuid().ToString("n");
            }
            
            // Secure by default: new shares are Restricted (only people with specific permission or invite)
            doc.PublicAccessLevel = "Restricted";
            await _context.SaveChangesAsync();

            var publicLink = Url.Action("Document", "Shared", new { area = "", id = id, token = doc.PublicShareToken }, HttpContext.Request.Scheme);

            return Json(new { success = true, publicLink = publicLink });
        }

        /// <summary>
        /// Updates the visibility of the "Unified Link" (Restricted, Viewer, or Editor).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> UpdatePublicAccess(int id, bool enabled, string level)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 2) return Forbid(); // Admin/Editor only

            var doc = await _context.Documents.FindAsync(id);
            if (doc == null || doc.WorkplaceID != CurrentWorkplace.WorkplaceID) return NotFound();

            if (enabled)
            {
                if (string.IsNullOrEmpty(doc.PublicShareToken))
                {
                    doc.PublicShareToken = Guid.NewGuid().ToString("n");
                }
                doc.PublicAccessLevel = level; // Viewer, Editor
            }
            else
            {
                // Unification: Instead of nulling the token, we set it to Restricted.
                // This means the link still exists but requires login.
                if (string.IsNullOrEmpty(doc.PublicShareToken))
                {
                    doc.PublicShareToken = Guid.NewGuid().ToString("n");
                }
                doc.PublicAccessLevel = "Restricted";
            }

            await _context.SaveChangesAsync();
            
            // Log
            var log = new AuditLog
            {
                WorkplaceID = CurrentWorkplace.WorkplaceID,
                Action = enabled ? "Public Share Updated" : "Public Share Disabled",
                EntityType = "Document",
                EntityID = doc.DocumentID,
                UserID = CurrentMembership.UserID,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                PerformedAt = DateTime.UtcNow,
                Details = enabled ? $"Enabled public sharing ({level})" : "Disabled public sharing"
            };
            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();

            var publicLink = Url.Action("Document", "Shared", new { area = "", id = id, token = doc.PublicShareToken }, HttpContext.Request.Scheme);

            return Json(new { success = true, publicLink = publicLink, accessLevel = doc.PublicAccessLevel });
        }

        /// <summary>
        /// Grants access to a specific email address. 
        /// If the user exists, sets a DocumentPermission. 
        /// If not, creates a DocumentShareInvitation for future registration.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> AddUserPermission(int id, string email, string role)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 2) return Forbid();

            var doc = await _context.Documents.FindAsync(id);
            if (doc == null || doc.WorkplaceID != CurrentWorkplace.WorkplaceID) return NotFound();

            // Ensure a token exists for the unified link system
            if (string.IsNullOrEmpty(doc.PublicShareToken))
            {
                doc.PublicShareToken = Guid.NewGuid().ToString("n");
                if (string.IsNullOrEmpty(doc.PublicAccessLevel)) doc.PublicAccessLevel = "Restricted";
                await _context.SaveChangesAsync();
            }

            var docLink = Url.Action("Document", "Shared", new { area = "", id = id, token = doc.PublicShareToken }, HttpContext.Request.Scheme);
            var inviterName = CurrentMembership != null
                ? (await _context.Users.FindAsync(CurrentMembership.UserID))?.DisplayName ?? "A team member"
                : "A team member";

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user != null)
            {
                // User exists — grant permission directly
                var existing = await _context.DocumentPermissions
                    .FirstOrDefaultAsync(p => p.DocumentID == id && p.UserID == user.UserID);

                if (existing != null)
                    existing.PermissionLevel = role;
                else
                    _context.DocumentPermissions.Add(new DocumentPermission
                    {
                        DocumentID = id,
                        UserID = user.UserID,
                        PermissionLevel = role,
                        RoleName = "User"
                    });

                await _context.SaveChangesAsync();

                // Send notification email
                try
                {
                    var emailBody = $@"
                        <div style='font-family: sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 16px;'>
                            <h2 style='color: #4f46e5; margin-top: 0;'>Document Shared With You</h2>
                            <p><strong>{inviterName}</strong> has shared the document <strong>'{doc.Title}'</strong> with you as a <strong>{role}</strong>.</p>
                            <div style='margin: 32px 0; text-align: center;'>
                                <a href='{docLink}' style='background-color: #4f46e5; color: #ffffff; padding: 14px 28px; border-radius: 8px; text-decoration: none; font-weight: bold; font-size: 15px;'>Open Document</a>
                            </div>
                            <p style='font-size: 13px; color: #64748b;'>You can access this document any time by visiting <a href='{docLink}'>{docLink}</a></p>
                        </div>";
                    await _emailSender.SendAsync(email, $"{inviterName} shared '{doc.Title}' with you", emailBody);
                }
                catch { /* non-fatal */ }

                return Json(new {
                    success = true,
                    inviteLink = docLink,
                    user = new {
                        userID = user.UserID,
                        userEmail = user.Email,
                        userDisplayName = user.DisplayName ?? user.Username,
                        role = role
                    }
                });
            }
            else
            {
                // User does not exist — store pending invite and send sign-up email
                var token = Guid.NewGuid().ToString("N");

                // Remove any existing pending invite for this email + doc
                var oldInvite = await _context.DocumentShareInvitations
                    .FirstOrDefaultAsync(i => i.DocumentID == id && i.Email == email && !i.IsAccepted);
                if (oldInvite != null) _context.DocumentShareInvitations.Remove(oldInvite);

                _context.DocumentShareInvitations.Add(new DocumentShareInvitation
                {
                    DocumentID = id,
                    Email = email,
                    PermissionLevel = role,
                    Token = token,
                    InvitedByUserID = CurrentMembership!.UserID,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                // Use the single universal link defined above

                try
                {
                    var emailBody = $@"
                        <div style='font-family: sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 16px;'>
                            <h2 style='color: #4f46e5; margin-top: 0;'>You've Been Invited to View a Document</h2>
                            <p><strong>{inviterName}</strong> has invited you to access the document <strong>'{doc.Title}'</strong> on FileMatrix as a <strong>{role}</strong>.</p>
                            <p>Create a free account (or sign in) to view it:</p>
                            <div style='margin: 32px 0; text-align: center;'>
                                <a href='{docLink}' style='background-color: #4f46e5; color: #ffffff; padding: 14px 28px; border-radius: 8px; text-decoration: none; font-weight: bold; font-size: 15px;'>Accept &amp; View Document</a>
                            </div>
                            <p style='font-size: 13px; color: #64748b;'>If the button doesn't work, copy this link: <a href='{docLink}'>{docLink}</a></p>
                        </div>";
                    await _emailSender.SendAsync(email, $"You've been invited to view '{doc.Title}'", emailBody);
                }
                catch { /* non-fatal */ }

                return Json(new { 
                    success = true, 
                    pending = true, 
                    inviteLink = docLink,
                    message = $"Invitation sent to {email}. They'll gain access after signing in." 
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RemoveUserPermission(int id, int userId)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 2) return Forbid();

            var perm = await _context.DocumentPermissions
                .FirstOrDefaultAsync(p => p.DocumentID == id && p.UserID == userId);

            if (perm != null)
            {
                _context.DocumentPermissions.Remove(perm);
                await _context.SaveChangesAsync();
            }

            return Json(new { success = true });
        }
    }
}
