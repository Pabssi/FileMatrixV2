using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FileMatrix_Pabiran_.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace FileMatrix_Pabiran_.Data
{
    /// <summary>
    /// DbInitializer: The System Bootstrapper.
    /// 
    /// RESPONSIBILITY: Ensures the environment is ready by:
    /// 1. Seeding security roles (RBAC foundation).
    /// 2. Provisioning the initial SuperAdmin account.
    /// 3. Initializing global system settings and infrastructure tasks.
    /// </summary>
    public static class DbInitializer
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = serviceProvider.GetRequiredService<UserManager<IdentityUser<int>>>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();

            // Ensure database is created (optional, usually handled by migrations, but good safety)
            // await context.Database.EnsureCreatedAsync(); 

            await SeedRolesAsync(roleManager);
            await SeedSuperAdminAsync(userManager, context);
            await SeedSystemSettingsAsync(context);
            await SeedSystemTasksAsync(context);
        }

        /// <summary>
        /// Seeds global configuration values that control platform behavior.
        /// </summary>
        private static async Task SeedSystemSettingsAsync(ApplicationDbContext context)
        {
            var defaultSettings = new List<SystemSetting>
            {
                new SystemSetting { Key = "PublicRegistration", Value = "true", Description = "Allow new users to register without an invitation." },
                new SystemSetting { Key = "WorkplaceCreation", Value = "true", Description = "Allow standard users to provision new organizations." },
                new SystemSetting { Key = "MaintenanceMode", Value = "false", Description = "Redirect all users to a maintenance page." },
                new SystemSetting { Key = "TwoFactorEnforcement", Value = "false", Description = "Enforce 2FA for all administrative accounts." },
                new SystemSetting { Key = "SessionTimeout", Value = "30", Description = "Session timeout in minutes." },
                new SystemSetting { Key = "PasswordComplexity", Value = "true", Description = "Enforce high-strength password requirements globally." }
            };

            foreach (var setting in defaultSettings)
            {
                if (!await context.SystemSettings.AnyAsync(s => s.Key == setting.Key))
                {
                    setting.LastUpdated = DateTime.UtcNow;
                    context.SystemSettings.Add(setting);
                }
            }

            await context.SaveChangesAsync();
        }

        private static async Task SeedRolesAsync(RoleManager<IdentityRole<int>> roleManager)
        {
            // Seed Roles
            string[] roleNames = { "SuperAdmin", "Admin", "User" };
            foreach (var roleName in roleNames)
            {
                var roleExist = await roleManager.RoleExistsAsync(roleName);
                if (!roleExist)
                {
                    await roleManager.CreateAsync(new IdentityRole<int>(roleName));
                }
            }
        }

        /// <summary>
        /// Provisions the default SuperAdmin. 
        /// CRITICAL: This method bridges the gap between ASP.NET Identity (AspNetUsers) 
        /// and the business-layer Users table.
        /// </summary>
        private static async Task SeedSuperAdminAsync(UserManager<IdentityUser<int>> userManager, ApplicationDbContext context)
        {
            // Seed SuperAdmin User
            var superAdminEmail = "admin@filematrix.com";
            var superAdminUser = await userManager.FindByEmailAsync(superAdminEmail);

            if (superAdminUser == null)
            {
                var newAdmin = new IdentityUser<int>
                {
                    UserName = superAdminEmail,
                    Email = superAdminEmail,
                    EmailConfirmed = true
                };

                var createPowerUser = await userManager.CreateAsync(newAdmin, "Admin123!");
                if (createPowerUser.Succeeded)
                {
                    await userManager.AddToRoleAsync(newAdmin, "SuperAdmin");
                    
                    // Also seed into the legacy Users table
                    if (!await context.Users.AnyAsync(u => u.Email == superAdminEmail))
                    {
                        var dmsUser = new User
                        {
                            UserID = newAdmin.Id, // Sync IDs if possible
                            Username = superAdminEmail,
                            Email = superAdminEmail,
                            PasswordHash = newAdmin.PasswordHash ?? "",
                            DisplayName = "Super Admin",
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow,
                            Role = 0 // 0 = SuperAdmin
                        };
                        context.Users.Add(dmsUser);
                        await context.SaveChangesAsync();
                    }
                }
            }
            else
            {
                // Ensure existing admin has the role
                if (!await userManager.IsInRoleAsync(superAdminUser, "SuperAdmin"))
                {
                    await userManager.AddToRoleAsync(superAdminUser, "SuperAdmin");
                }

                // Ensure they exist in the legacy table too
                if (!await context.Users.AnyAsync(u => u.Email == superAdminUser.Email))
                {
                    var dmsUser = new User
                    {
                        UserID = superAdminUser.Id,
                        Username = superAdminUser.UserName,
                        Email = superAdminUser.Email,
                        PasswordHash = superAdminUser.PasswordHash ?? "",
                        DisplayName = "Super Admin",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        Role = 0
                    };
                    context.Users.Add(dmsUser);
                    await context.SaveChangesAsync();
                }
            }

            // Seed a legacy-only account (not in AspNetUsers)
            var legacyEmail = "legacy@filematrix.com";
            if (!await context.Users.AnyAsync(u => u.Email == legacyEmail))
            {
                var legacyUser = new User
                {
                    Username = "legacy_user",
                    Email = legacyEmail,
                    PasswordHash = "LEGACY_PASSWORD_HASH",
                    DisplayName = "Legacy User",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    Role = 3 // Viewer
                };
                context.Users.Add(legacyUser);
                await context.SaveChangesAsync();
            }
        }
        /// <summary>
        /// Registers background maintenance jobs that SuperAdmins can monitor or run.
        /// </summary>
        private static async Task SeedSystemTasksAsync(ApplicationDbContext context)
        {
            var tasks = new List<SystemInfrastructureTask>
            {
                new SystemInfrastructureTask 
                { 
                    Key = "normalize-usernames", 
                    Name = "Normalize Usernames", 
                    Description = "Ensures all usernames are stored in a standard format (lowercase/trimmmed).",
                    Status = "Healthy"
                },
                new SystemInfrastructureTask 
                { 
                    Key = "sync-storage", 
                    Name = "Sync Storage Metadata", 
                    Description = "Re-calculates file sizes for all documents in the system based on actual versions.",
                    Status = "Pending"
                },
                new SystemInfrastructureTask 
                { 
                    Key = "role-consistency", 
                    Name = "Role Consistency Check", 
                    Description = "Verifies that all users have at least one valid role and synchronizes with Identity.",
                    Status = "Healthy"
                }
            };

            foreach (var task in tasks)
            {
                if (!await context.SystemInfrastructureTasks.AnyAsync(t => t.Key == task.Key))
                {
                    task.LastRun = DateTime.UtcNow.AddDays(-1); // Initial state
                    context.SystemInfrastructureTasks.Add(task);
                }
            }
            await context.SaveChangesAsync();
        }
    }
}
