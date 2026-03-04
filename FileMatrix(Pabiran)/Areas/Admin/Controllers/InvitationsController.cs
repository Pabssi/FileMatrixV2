using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FileMatrix_Pabiran_.Models;
using FileMatrix_Pabiran_.Data;
using System.Security.Claims;

namespace FileMatrix_Pabiran_.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Route("Admin/Invitations")]
    /// <summary>
    /// InvitationsController: Workplace Onboarding & Growth.
    /// 
    /// RESPONSIBILITY: Manages the generation and delivery of invitation links 
    /// used to onboard new members into the specific workplace.
    /// </summary>
    public class InvitationsController : BaseAdminController
    {
        private readonly FileMatrix_Pabiran_.Services.EmailSenderService _emailSender;

        public InvitationsController(ApplicationDbContext context, FileMatrix_Pabiran_.Services.EmailSenderService emailSender) : base(context)
        {
            _emailSender = emailSender;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            if (CurrentWorkplace == null) return RedirectToAction("Index", "Organizations", new { area = "" });
            if (CurrentMembership == null || CurrentMembership.RoleID > 1) return Forbid();

            var invitations = await _context.WorkplaceInvitations
                .Where(i => i.WorkplaceID == CurrentWorkplace.WorkplaceID)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();

            ViewBag.ActiveLinks = invitations.Count(i => i.Status == "pending" && (i.UsageLimit == null || i.UsageCount < i.UsageLimit) && (i.ExpiresAt == null || i.ExpiresAt > DateTime.UtcNow));
            ViewBag.ExpiredLinks = invitations.Count(i => i.Status == "expired" || (i.ExpiresAt != null && i.ExpiresAt < DateTime.UtcNow) || (i.UsageLimit != null && i.UsageCount >= i.UsageLimit));

            return View(invitations);
        }

        /// <summary>
        /// Generates a unique onboarding token/code and optionally sends an 
        /// invitation email to the recipient.
        /// </summary>
        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string? email, int? usageLimit, int roleId = 3, int expiryDays = 7)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 1) return Forbid();

            var token = Guid.NewGuid().ToString("N");
            var code = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();

            var invitation = new WorkplaceInvitation
            {
                WorkplaceID = CurrentWorkplace.WorkplaceID,
                InvitedByUserID = CurrentMembership.UserID,
                Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
                Token = token,
                Code = code,
                RoleID = roleId,
                UsageLimit = usageLimit > 0 ? usageLimit : null,
                UsageCount = 0,
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiryDays > 0 ? DateTime.UtcNow.AddDays(expiryDays) : null
            };

            _context.WorkplaceInvitations.Add(invitation);
            await _context.SaveChangesAsync();

            // Send email if a recipient is specified
            if (!string.IsNullOrEmpty(invitation.Email))
            {
                try
                {
                    var joinUrl = Url.Action("Join", "Organizations", new { area = "", token = token }, protocol: Request.Scheme);
                    var emailBody = $@"
                        <div style='font-family: sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 16px;'>
                            <h2 style='color: #4f46e5;'>Workspace Invitation</h2>
                            <p>You have been invited to join the workplace <strong>'{CurrentWorkplace.Name}'</strong> on FileMatrix.</p>
                            <p>Click the button below to accept the invitation and join your team:</p>
                            <div style='margin: 32px 0; text-align: center;'>
                                <a href='{joinUrl}' style='background-color: #4f46e5; color: #ffffff; padding: 12px 24px; border-radius: 8px; text-decoration: none; font-weight: bold;'>Accept Invitation</a>
                            </div>
                            <p style='font-size: 14px; color: #64748b;'>This invitation was sent by {User.Identity?.Name}.</p>
                            <hr style='border: 0; border-top: 1px solid #e2e8f0; margin: 24px 0;' />
                            <p style='font-size: 12px; color: #94a3b8;'>If you didn't expect this invitation, you can safely ignore this email.</p>
                        </div>";

                    await _emailSender.SendAsync(invitation.Email, $"Invitation to join {CurrentWorkplace.Name}", emailBody);
                }
                catch (Exception ex)
                {
                    // Log error but don't fail the whole request
                    TempData["ErrorMessage"] = "Invitation created, but failed to send email: " + ex.Message;
                    return RedirectToAction(nameof(Index));
                }
            }

            TempData["SuccessMessage"] = string.IsNullOrEmpty(invitation.Email) 
                ? "Invitation link generated successfully." 
                : $"Invitation sent to {invitation.Email} successfully.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Delete/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 1) return Forbid();

            var invitation = await _context.WorkplaceInvitations
                .FirstOrDefaultAsync(i => i.InvitationID == id && i.WorkplaceID == CurrentWorkplace.WorkplaceID);

            if (invitation == null) return NotFound();

            _context.WorkplaceInvitations.Remove(invitation);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Invitation deleted.";
            return RedirectToAction(nameof(Index));
        }
    }
}

