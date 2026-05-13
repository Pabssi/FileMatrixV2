using FileMatrix_Pabiran_.Data;
using FileMatrix_Pabiran_.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileMatrix_Pabiran_.Services
{
    /// <summary>
    /// RetentionWorker: The Background Hygiene Engine.
    /// 
    /// RESPONSIBILITY: Periodically (every 24h) scans all workplaces for 
    /// Documents that have exceeded their retention thresholds (Archive/Delete).
    /// PIPELINE: Auto-Archive -> 7-Day Alert -> Auto-Delete.
    /// </summary>
    public class RetentionWorker : BackgroundService
    {
        private readonly ILogger<RetentionWorker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

        public RetentionWorker(ILogger<RetentionWorker> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RetentionWorker is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPoliciesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing retention policies.");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("RetentionWorker is stopping.");
        }

        private async Task ProcessPoliciesAsync()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                // Simple Query: Get every workplace that has an 'Active' retention policy enabled.
                var activePolicies = await dbContext.RetentionPolicies
                    .Where(p => p.IsEnabled)
                    .ToListAsync();

                _logger.LogInformation($"Found {activePolicies.Count} active retention policies to process.");

                foreach (var policy in activePolicies)
                {
                    await ApplyPolicyToWorkplace(scope.ServiceProvider, policy);
                }
            }
        }

        private async Task ApplyPolicyToWorkplace(IServiceProvider serviceProvider, RetentionPolicy policy)
        {
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTime.UtcNow;

            // STAGE 1: Auto-Archive. 
            // Moves documents from Published/Draft to Archived if they haven't been 
            // touched within the archival window.
            if (policy.AutoArchiveAfterDays.HasValue)
            {
                var threshold = now.AddDays(-policy.AutoArchiveAfterDays.Value);
                // Simple Query: Find all documents that are old enough to be archived based on the policy.
                var docsToArchive = await context.Documents
                    .Where(d => d.WorkplaceID == policy.WorkplaceID && 
                                d.Status != "Archived" && 
                                (d.UpdatedAt ?? d.CreatedAt) < threshold)
                    .ToListAsync();

                if (docsToArchive.Any())
                {
                    _logger.LogInformation($"Policy {policy.ID}: Archiving {docsToArchive.Count} documents in workplace {policy.WorkplaceID}");
                    
                    foreach (var doc in docsToArchive)
                    {
                        doc.Status = "Archived";
                        doc.ArchivedAt = now;

                        context.AuditLogs.Add(new AuditLog
                        {
                            WorkplaceID = policy.WorkplaceID,
                            Action = "Auto-Archive",
                            EntityType = "Document",
                            EntityID = doc.DocumentID,
                            PerformedAt = now,
                            Details = $"Automatically archived document '{doc.Title}' based on retention policy (>{policy.AutoArchiveAfterDays} days)."
                        });
                    }
                    await context.SaveChangesAsync();
                }
            }

            // STAGE 2 & 3: Archival Lifecycle. 
            // Triggers a 7-day warning email before permanently purging documents 
            // that have exceeded the archive deletion threshold.
            if (policy.AutoDeleteAfterDays.HasValue)
            {
                var deleteThreshold = now.AddDays(-policy.AutoDeleteAfterDays.Value);
                var notifyThreshold = now.AddDays(-(policy.AutoDeleteAfterDays.Value - 7));

                // 2. Process Deletions
                var docsToDelete = await context.Documents
                    .Where(d => d.WorkplaceID == policy.WorkplaceID && 
                                d.Status == "Archived" && 
                                d.ArchivedAt < deleteThreshold)
                    .ToListAsync();

                if (docsToDelete.Any())
                {
                    var cloudinary = serviceProvider.GetRequiredService<CloudinaryService>();
                    _logger.LogInformation($"Policy {policy.ID}: Deleting {docsToDelete.Count} archived documents in workplace {policy.WorkplaceID}");
                    
                    foreach (var doc in docsToDelete)
                    {
                        // Delete all physical files for this document (all versions)
                        var versions = await context.DocumentVersions
                            .Where(v => v.DocumentID == doc.DocumentID)
                            .ToListAsync();

                        foreach (var version in versions)
                        {
                            if (!string.IsNullOrEmpty(version.ExternalPublicID))
                            {
                                try
                                {
                                    await cloudinary.DeleteAsync(version.ExternalPublicID);
                                    _logger.LogInformation($"Deleted Cloudinary file: {version.ExternalPublicID}");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Failed to delete Cloudinary file {version.ExternalPublicID}");
                                }
                            }
                        }

                        context.AuditLogs.Add(new AuditLog
                        {
                            WorkplaceID = policy.WorkplaceID,
                            Action = "Auto-Delete",
                            EntityType = "Document",
                            EntityID = doc.DocumentID,
                            PerformedAt = now,
                            Details = $"Automatically deleted document '{doc.Title}' and its Cloudinary assets based on retention policy (>{policy.AutoDeleteAfterDays} days in archive)."
                        });

                        context.Documents.Remove(doc);
                    }
                    await context.SaveChangesAsync();
                }

                // 3. Process 7-Day Notifications
                var emailSender = serviceProvider.GetRequiredService<EmailSenderService>();
                var docsToNotify = await context.Documents
                    .Where(d => d.WorkplaceID == policy.WorkplaceID && 
                                d.Status == "Archived" && 
                                d.ArchivedAt < notifyThreshold && 
                                !d.RetentionNoticeSent)
                    .ToListAsync();

                if (docsToNotify.Any())
                {
                    _logger.LogInformation($"Policy {policy.ID}: Sending deletion alerts for {docsToNotify.Count} documents in workplace {policy.WorkplaceID}");
                    
                    foreach (var doc in docsToNotify)
                    {
                        // Identify recipients: Document owner and/or workplace admins
                        var recipientEmail = doc.CreatedBy?.Email;
                        if (string.IsNullOrEmpty(recipientEmail))
                        {
                            // Fallback to first workplace admin if owner not found
                            var adminMember = await context.WorkplaceMembers
                                .Where(m => m.WorkplaceID == policy.WorkplaceID && m.RoleID == 1)
                                .FirstOrDefaultAsync();
                            
                            if (adminMember != null)
                            {
                                var admin = await context.Users.FindAsync(adminMember.UserID);
                                recipientEmail = admin?.Email;
                            }
                        }

                        if (!string.IsNullOrEmpty(recipientEmail))
                        {
                            var deletionDate = doc.ArchivedAt.Value.AddDays(policy.AutoDeleteAfterDays.Value);
                            var subject = $"Deletion Alert: Document '{doc.Title}' is scheduled for removal";
                            var body = $@"
                                <h3>FileMatrix Deletion Alert</h3>
                                <p>Your document <strong>'{doc.Title}'</strong> is scheduled for permanent deletion in 7 days.</p>
                                <p><strong>Scheduled Deletion:</strong> {deletionDate:MMM dd, yyyy}</p>
                                <p>If you wish to prevent this, please restore the document from the archive section.</p>
                                <hr/>
                                <p><small>This is an automated notification based on your workplace retention policy.</small></p>";

                            await emailSender.SendAsync(recipientEmail, subject, body);
                            doc.RetentionNoticeSent = true;

                            context.AuditLogs.Add(new AuditLog
                            {
                                WorkplaceID = policy.WorkplaceID,
                                Action = "Retention Notification",
                                EntityType = "Document",
                                EntityID = doc.DocumentID,
                                PerformedAt = now,
                                Details = $"Sent 7-day deletion alert to {recipientEmail} for document '{doc.Title}'."
                            });
                        }
                    }
                    await context.SaveChangesAsync();
                }
            }
        }
    }
}
