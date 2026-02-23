using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using FileMatrix_Pabiran_.Models;

namespace FileMatrix_Pabiran_.Controllers
{
    // Note: project has Identity scaffolding under Areas/Identity; these actions provide a simple controller-backed flow
    public class AccountController : Controller
    {
        private readonly SignInManager<IdentityUser<int>> _signInManager;
        private readonly UserManager<IdentityUser<int>> _userManager;
        private readonly FileMatrix_Pabiran_.Data.ApplicationDbContext _db;
        private readonly FileMatrix_Pabiran_.Services.EmailSenderService _emailSender;

        public AccountController(
            SignInManager<IdentityUser<int>> signInManager, 
            UserManager<IdentityUser<int>> userManager, 
            FileMatrix_Pabiran_.Data.ApplicationDbContext db,
            FileMatrix_Pabiran_.Services.EmailSenderService emailSender)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _db = db;
            _emailSender = emailSender;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            // Redirect to the site root (landing page)
            // We intentionally send users to the landing page instead of a standalone login page.
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            try
            {
                var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(Request.Form["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

                if (!ModelState.IsValid)
                {
                    if (isAjax)
                    {
                        var knownProps = new[] { nameof(LoginViewModel.UsernameOrEmail), nameof(LoginViewModel.Password), nameof(LoginViewModel.RememberMe) };
                        var errorsByField = new System.Collections.Generic.Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in ModelState.Where(kv => kv.Value?.Errors?.Count > 0))
                        {
                            var key = kv.Key ?? string.Empty;
                            var matched = knownProps.FirstOrDefault(p => !string.IsNullOrEmpty(key) && key.EndsWith(p, StringComparison.OrdinalIgnoreCase));
                            var outKey = matched ?? key;
                            errorsByField[outKey] = kv.Value.Errors.Select(e => e.ErrorMessage).ToArray();
                        }
                        return Json(new { success = false, errors = errorsByField });
                    }

                    return View(model);
                }

                // allow username OR email (more robust: trim + fallback to legacy email lookup)
                var rawInput = model.UsernameOrEmail ?? string.Empty;
                var identifier = rawInput.Trim();

                User? user = null;

                // If it looks like an email, prefer an email-based lookup first
                if (!string.IsNullOrEmpty(identifier) && identifier.Contains("@"))
                {
                    // Primary: standard Identity email lookup (uses NormalizedEmail)
                    var identityUser = await _userManager.FindByEmailAsync(identifier);

                    // Fallback: if that failed, try a direct case-insensitive match on
                    // the Users.Email column.
                    if (identityUser == null)
                    {
                        try
                        {
                            var emailLower = identifier.ToLowerInvariant();
                            user = await _db.Users
                                .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == emailLower);
                        }
                        catch { }
                    }
                    else
                    {
                        // Look up our custom User from identity user's email
                        user = await _db.Users.FirstOrDefaultAsync(u => u.Email == identityUser.Email);
                    }
                }

                // If we still don't have a user, or it wasn't an email, try username
                if (user == null && !string.IsNullOrEmpty(identifier))
                {
                    user = await _db.Users.FirstOrDefaultAsync(u => u.Username == identifier);
                }
                if (user == null)
                {
                    // map to the UsernameOrEmail field for inline display
                    ModelState.AddModelError(nameof(LoginViewModel.UsernameOrEmail), "Invalid username or email");
                    if (isAjax)
                    {
                        return Json(new { success = false, errors = new System.Collections.Generic.Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase) { { nameof(LoginViewModel.UsernameOrEmail), new[] { "Invalid username or email" } } } });
                    }
                    return View(model);
                }

                // Look up the IdentityUser for sign-in
                var identityUserForSignIn = await _userManager.FindByEmailAsync(user.Email ?? "");
                if (identityUserForSignIn == null)
                {
                    identityUserForSignIn = await _userManager.FindByNameAsync(user.Username ?? "");
                }
                if (identityUserForSignIn == null)
                {
                    ModelState.AddModelError(nameof(LoginViewModel.UsernameOrEmail), "Account not found in identity system");
                    if (isAjax)
                    {
                        return Json(new { success = false, errors = new System.Collections.Generic.Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase) { { nameof(LoginViewModel.UsernameOrEmail), new[] { "Account not found" } } } });
                    }
                    return View(model);
                }

                if (!identityUserForSignIn.EmailConfirmed)
                {
                    ModelState.AddModelError(string.Empty, "You must confirm your email before logging in.");
                    if (isAjax)
                    {
                        return Json(new { success = false, errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { { string.Empty, new[] { "Please confirm your email before logging in." } } } });
                    }
                    return View(model);
                }

                if (!user.IsActive)
                {
                    ModelState.AddModelError(nameof(LoginViewModel.UsernameOrEmail), "Your account has been suspended by a platform administrator.");
                    if (isAjax)
                    {
                        return Json(new { success = false, errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { { nameof(LoginViewModel.UsernameOrEmail), new[] { "Your account has been suspended by a platform administrator." } } } });
                    }
                    return View(model);
                }

                var result = await _signInManager.PasswordSignInAsync(identityUserForSignIn, model.Password, model.RememberMe, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    
                    try
                    {
                        var userId = user.UserID;
                        var exists = await _db.Users.AnyAsync(u => u.UserID == userId);
                        if (!exists)
                        {
                            var dmsUser = new User
                            {
                                UserID = userId,
                                Username = user.Username,
                                Email = user.Email,
                                DisplayName = user.Username,
                                IsActive = true,
                                CreatedAt = DateTime.UtcNow
                            };
                            _db.Users.Add(dmsUser);
                            await _db.SaveChangesAsync();
                        }
                    }
                    catch
                    {
                        // Swallow exceptions to avoid breaking login flow; membership
                        // check may still run and fall back to onboarding if needed.
                    }

                    // Determine where to redirect
                    bool hasActiveOrg = false;
                    try
                    {
                        hasActiveOrg = await _db.WorkplaceMembers
                            .AnyAsync(m => m.UserID == user.UserID);

                        // If no membership found, also consider workplaces the user created
                        if (!hasActiveOrg)
                        {
                            hasActiveOrg = await _db.Workplaces
                                .AnyAsync(o => o.CreatedByUserID == user.UserID && o.IsActive);
                        }
                    }
                    catch
                    {
                        // ignore DB errors here and fall back to onboarding
                        hasActiveOrg = false;
                    }

                    var defaultRedirect = hasActiveOrg ? Url.Action("Index", "Admin", new { area = "Admin" }) : Url.Action("Index", "Organizations");

                    // Priority redirect for SuperAdmins
                    if (await _userManager.IsInRoleAsync(identityUserForSignIn, "SuperAdmin"))
                    {
                        defaultRedirect = Url.Action("Index", "SuperAdmin", new { area = "SuperAdmin" });
                    }

                    // Redeem any pending document share invite
                    string? docShareRedirect = null;
                    if (Request.Cookies.TryGetValue("PendingDocInvite", out var pendingToken) && !string.IsNullOrEmpty(pendingToken))
                    {
                        try
                        {
                            var pendingInvite = await _db.DocumentShareInvitations
                                .FirstOrDefaultAsync(i => i.Token == pendingToken && !i.IsAccepted);
                            if (pendingInvite != null)
                            {
                                var existingPerm = await _db.DocumentPermissions
                                    .FirstOrDefaultAsync(p => p.DocumentID == pendingInvite.DocumentID && p.UserID == user.UserID);
                                if (existingPerm == null)
                                {
                                    _db.DocumentPermissions.Add(new DocumentPermission
                                    {
                                        DocumentID = pendingInvite.DocumentID,
                                        UserID = user.UserID,
                                        PermissionLevel = pendingInvite.PermissionLevel,
                                        RoleName = "User"
                                    });
                                }
                                pendingInvite.IsAccepted = true;
                                pendingInvite.AcceptedAt = DateTime.UtcNow;
                                await _db.SaveChangesAsync();
                                docShareRedirect = Url.Action("Document", "Shared", new { area = "", id = pendingInvite.DocumentID });
                            }
                        }
                        catch { }
                        Response.Cookies.Delete("PendingDocInvite");
                    }

                    if (isAjax)
                    {
                        // return a redirectUrl so the client can navigate as needed
                        var redirect = docShareRedirect ?? (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : defaultRedirect);
                        return Json(new { success = true, redirectUrl = redirect });
                    }

                    if (docShareRedirect != null) return Redirect(docShareRedirect);
                    if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                        return Redirect(returnUrl);

                    return Redirect(defaultRedirect);
                }

                ModelState.AddModelError(nameof(LoginViewModel.Password), "Wrong password");
                if (isAjax)
                {
                    var errorsByField = new System.Collections.Generic.Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase);
                    errorsByField[nameof(LoginViewModel.Password)] = new[] { "Wrong password" };
                    return Json(new { success = false, errors = errorsByField });
                }

                return View(model);
            }
            catch (Exception ex)
            {
                var isAjaxErr = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(Request.Form["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
                if (isAjaxErr)
                {
                    // return a field->messages object with the exception message so client can show it
                    return Json(new { success = false, errors = new System.Collections.Generic.Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase) { { string.Empty, new[] { ex.Message } } } });
                }

                ModelState.AddModelError(string.Empty, "An unexpected error occurred. Please try again later.");
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult Register()
        {
            // Redirect to the home/landing page which will open the auth modal register tab.
            if (Url != null)
            {
                return RedirectToAction("Index", "Home", new { auth = "register" });
            }
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            try
            {
                if (!ModelState.IsValid) return View(model);

                // Pre-check for duplicate username/email to provide field-specific errors
                var existingByName = await _userManager.FindByNameAsync(model.Username);
                if (existingByName != null)
                {
                    ModelState.AddModelError(nameof(RegisterViewModel.Username), $"The Username '{model.Username}' is already taken.");
                }
                var existingByEmail = await _userManager.FindByEmailAsync(model.Email);
                if (existingByEmail != null)
                {
                    ModelState.AddModelError(nameof(RegisterViewModel.Email), $"{model.Email} is already registered.");
                }
                if (!ModelState.IsValid)
                {
                    var isAjaxCheck = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(Request.Form["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
                    if (isAjaxCheck)
                    {
                        var knownProps = new[] { nameof(RegisterViewModel.Username), nameof(RegisterViewModel.Email), nameof(RegisterViewModel.Password), nameof(RegisterViewModel.ConfirmPassword) };
                        var errorsByField = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in ModelState.Where(kv => kv.Value?.Errors?.Count > 0))
                        {
                            var key = kv.Key ?? string.Empty;
                            var matched = knownProps.FirstOrDefault(p => !string.IsNullOrEmpty(key) && key.EndsWith(p, StringComparison.OrdinalIgnoreCase));
                            var outKey = matched ?? key;
                            errorsByField[outKey] = kv.Value.Errors.Select(e => e.ErrorMessage).ToArray();
                        }
                        return Json(new { success = false, errors = errorsByField });
                    }

                    return View(model);
                }

                var identityNewUser = new IdentityUser<int> { UserName = model.Username, Email = model.Email };
                identityNewUser.EmailConfirmed = false;
                var result = await _userManager.CreateAsync(identityNewUser, model.Password);
                if (result.Succeeded)
                {
                    // Generate confirmation token
                    var token = await _userManager.GenerateEmailConfirmationTokenAsync(identityNewUser);
                    var callbackUrl = Url.Action("ConfirmEmail", "Account", new { userId = identityNewUser.Id, token = token }, protocol: Request.Scheme);
                    var logoUrl = Url.Content("~/images/FileMatrix.png");
                    var absoluteLogoUrl = $"{Request.Scheme}://{Request.Host}{logoUrl}";

                    // Send verification email
                    var emailBody = $@"
                        <div style='font-family: sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1e293b; border: 1px solid #e2e8f0; border-radius: 16px;'>
                            <div style='text-align: center; margin-bottom: 24px;'>
                                <img src='{absoluteLogoUrl}' alt='FileMatrix Logo' style='height: 48px;' />
                            </div>
                            <h2 style='color: #4f46e5; margin-bottom: 24px; text-align: center;'>Welcome to FileMatrix!</h2>
                            <p style='font-size: 16px; line-height: 1.6;'>Thank you for joining us. To finalize your account and start managing your documents, please confirm your email address by clicking the button below:</p>
                            <div style='margin-top: 32px; margin-bottom: 32px; text-align: center;'>
                                <a href='{callbackUrl}' style='background-color: #4f46e5; color: #ffffff; padding: 14px 28px; border-radius: 12px; text-decoration: none; font-weight: 700; display: inline-block;'>Verify My Email</a>
                            </div>
                            <p style='font-size: 14px; color: #64748b;'>If the button doesn't work, copy and paste this link into your browser:</p>
                            <p style='font-size: 14px; color: #94a3b8; word-break: break-all;'>{callbackUrl}</p>
                            <hr style='border: 0; border-top: 1px solid #e2e8f0; margin: 32px 0;' />
                            <p style='font-size: 12px; color: #94a3b8; text-align: center;'>If you didn't create an account, you can safely ignore this email.</p>
                        </div>";

                    await _emailSender.SendAsync(model.Email, "Verify your FileMatrix Account", emailBody);

                    // Create DMS user record (inactive until confirmed or just marked as active for lookup)
                    try
                    {
                        var dmsUser = new FileMatrix_Pabiran_.Models.User
                        {
                            Username = model.Username,
                            Email = model.Email,
                            PasswordHash = identityNewUser.PasswordHash ?? "",
                            DisplayName = model.Username,
                            IsActive = false, // Set to false since email is not confirmed
                            CreatedAt = DateTime.UtcNow
                        };
                        _db.Users.Add(dmsUser);
                        await _db.SaveChangesAsync();
                    }
                    catch { }

                    var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(Request.Form["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
                    
                    if (isAjax)
                    {
                        return Json(new { success = true, next = "confirmation", message = "Registration successful. Please check your email to verify your account." });
                    }

                    return RedirectToAction("RegisterConfirmation");
                }

                // Map Identity errors to appropriate model fields where possible so
                // they display inline (e.g. password policy errors under the
                // password input) instead of only in the summary.
                foreach (var err in result.Errors)
                {
                    var desc = err?.Description ?? string.Empty;
                    var key = string.Empty;

                    // If the error description references email or is the "Email '{0}' is already taken." pattern
                    if (desc.IndexOf("email", StringComparison.OrdinalIgnoreCase) >= 0 || (desc.IndexOf("already", StringComparison.OrdinalIgnoreCase) >= 0 && desc.IndexOf("taken", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        key = nameof(RegisterViewModel.Email);
                    }
                    // common Identity password error phrases -> show under password field
                    else if (desc.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 || desc.IndexOf("lowercase", StringComparison.OrdinalIgnoreCase) >= 0 || desc.IndexOf("uppercase", StringComparison.OrdinalIgnoreCase) >= 0 || desc.IndexOf("non alphanumeric", StringComparison.OrdinalIgnoreCase) >= 0 || desc.IndexOf("digit", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        key = nameof(RegisterViewModel.Password);
                    }
                    else if (desc.IndexOf("username", StringComparison.OrdinalIgnoreCase) >= 0 || desc.IndexOf("user name", StringComparison.OrdinalIgnoreCase) >= 0 || desc.IndexOf("user name", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        key = nameof(RegisterViewModel.Username);
                    }
                    else
                    {
                        key = string.Empty;
                    }

                    ModelState.AddModelError(key, desc);
                }

                var isAjaxFallback = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(Request.Form["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
                if (isAjaxFallback)
                {
                    // Normalize ModelState keys to RegisterViewModel property names so
                    // client-side can target [data-valmsg-for] elements reliably.
                    var knownProps = new[] { nameof(RegisterViewModel.Username), nameof(RegisterViewModel.Email), nameof(RegisterViewModel.Password), nameof(RegisterViewModel.ConfirmPassword) };
                    var errorsByField = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in ModelState.Where(kv => kv.Value?.Errors?.Count > 0))
                    {
                        var key = kv.Key ?? string.Empty;
                        var matched = knownProps.FirstOrDefault(p => !string.IsNullOrEmpty(key) && key.EndsWith(p, StringComparison.OrdinalIgnoreCase));
                        var outKey = matched ?? key;
                        errorsByField[outKey] = kv.Value.Errors.Select(e => e.ErrorMessage).ToArray();
                    }
                    return Json(new { success = false, errors = errorsByField });
                }

                return View(model);
            }
            catch (DbUpdateException dbEx)
            {
                // Database update failed � likely a unique constraint violation (email/username).
                var baseMsg = dbEx.GetBaseException()?.Message ?? dbEx.Message;
                var isAjaxErr = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(Request.Form["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

                // Try to detect common unique/index violation text and map to the email field
                if (!string.IsNullOrEmpty(baseMsg) && (baseMsg.Contains("IX_Users_Email") || baseMsg.ToLowerInvariant().Contains("duplicate") || baseMsg.ToLowerInvariant().Contains("unique" ) || baseMsg.ToLowerInvariant().Contains("cannot insert")))
                {
                    ModelState.AddModelError(nameof(RegisterViewModel.Email), $"{model.Email} is already registered.");
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "An unexpected database error occurred. Please try again later.");
                }

                if (isAjaxErr)
                {
                    var knownProps = new[] { nameof(RegisterViewModel.Username), nameof(RegisterViewModel.Email), nameof(RegisterViewModel.Password), nameof(RegisterViewModel.ConfirmPassword) };
                    var errorsByField = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in ModelState.Where(kv => kv.Value?.Errors?.Count > 0))
                    {
                        var key = kv.Key ?? string.Empty;
                        var matched = knownProps.FirstOrDefault(p => !string.IsNullOrEmpty(key) && key.EndsWith(p, StringComparison.OrdinalIgnoreCase));
                        var outKey = matched ?? key;
                        errorsByField[outKey] = kv.Value.Errors.Select(e => e.ErrorMessage).ToArray();
                    }
                    return Json(new { success = false, errors = errorsByField });
                }

                return View(model);
            }
            catch (Exception ex)
            {
                // For AJAX requests return JSON with the exception message to aid debugging;
                // otherwise re-display the view with a generic message.
                var isAjaxErr = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(Request.Form["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
                if (isAjaxErr)
                {
                    var knownProps = new[] { nameof(RegisterViewModel.Username), nameof(RegisterViewModel.Email), nameof(RegisterViewModel.Password), nameof(RegisterViewModel.ConfirmPassword) };
                    var errorsByField = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in ModelState.Where(kv => kv.Value?.Errors?.Count > 0))
                    {
                        var key = kv.Key ?? string.Empty;
                        var matched = knownProps.FirstOrDefault(p => !string.IsNullOrEmpty(key) && key.EndsWith(p, StringComparison.OrdinalIgnoreCase));
                        var outKey = matched ?? key;
                        errorsByField[outKey] = kv.Value.Errors.Select(e => e.ErrorMessage).ToArray();
                    }
                    // include exception message as general error if no field errors
                    if (!errorsByField.Any())
                    {
                        errorsByField[string.Empty] = new[] { ex.Message };
                    }
                    return Json(new { success = false, errors = errorsByField });
                }

                ModelState.AddModelError(string.Empty, "An unexpected error occurred. Please try again later.");
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult RegisterConfirmation()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(int userId, string token)
        {
            if (userId <= 0 || string.IsNullOrEmpty(token))
            {
                return RedirectToAction("Index", "Home");
            }

            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{userId}'.");
            }

            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (result.Succeeded)
            {
                // Also activate the DMS user record
                var dmsUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == user.Email);
                if (dmsUser != null)
                {
                    dmsUser.IsActive = true;
                    await _db.SaveChangesAsync();
                }
            }

            ViewBag.Succeeded = result.Succeeded;
            return View("ConfirmEmail");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SwitchWorkplace(int id)
        {
            // Look up the custom User entity by email to get the correct UserID
            var email = User.FindFirstValue(ClaimTypes.Email);
            var customUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
            
            if (customUser == null)
            {
                return RedirectToAction("Index", "Home");
            }

            // Verify membership and that the workplace is active
            var workplace = await _db.Workplaces.FindAsync(id);
            var membership = await _db.WorkplaceMembers
                .FirstOrDefaultAsync(m => m.UserID == customUser.UserID && m.WorkplaceID == id);

            if (membership == null || workplace == null || !workplace.IsActive)
            {
                TempData["ErrorMessage"] = "This workplace is currently unavailable or suspended.";
                return RedirectToAction("Index", "Organizations");
            }

            // Set cookie preference
            Response.Cookies.Append("LastWorkplaceID", id.ToString(), new CookieOptions
            {
                Expires = DateTime.UtcNow.AddDays(365),
                HttpOnly = true,
                IsEssential = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax
            });

            return RedirectToAction("Index", "Admin", new { area = "Admin" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            // Ensure the Identity cookie and any auth cookies are removed.
            try
            {
                await _signInManager.SignOutAsync();
                // Sign out explicit authentication schemes as a fallback
                await HttpContext.SignOutAsync(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);
            }
            catch
            {
                // ignore failures but attempt cookie deletion below
            }

            // Try to remove common cookie names used by Identity/Cookie auth to ensure
            // client no longer sends an authentication cookie after logout.
            try { Response.Cookies.Delete(".AspNetCore.Identity.Application"); } catch { }
            try { Response.Cookies.Delete(".AspNetCore.Cookies"); } catch { }

            return RedirectToAction("Index", "Home");
        }

        // Sometimes clients may trigger a GET request to the logout URL (for example
        // via an anchor or external link). Provide a GET endpoint that performs sign
        // out and redirects to home to avoid HTTP 405 responses. Note: logout via
        // GET is generally discouraged for CSRF reasons but this app also supports
        // the POST-based logout with antiforgery token.
        [HttpGet]
        [ActionName("Logout")]
        public async Task<IActionResult> LogoutGet()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

    }
}
