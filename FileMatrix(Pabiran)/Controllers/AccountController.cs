using System;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using FileMatrix_Pabiran_.Models;

namespace FileMatrix_Pabiran_.Controllers
{
    // Note: project has Identity scaffolding under Areas/Identity; these actions provide a simple controller-backed flow
    /// <summary>
    /// AccountController: The Identity-DMS Bridge.
    /// 
    /// RESPONSIBILITY: Manages the authentication lifecycle (Login, Register, Logout) 
    /// and synchronizes the ASP.NET Core Identity system with the DMS 'User' entity.
    /// DESIGN: Supports 'Unified Login' (Username/Email) and 'Invite-First' onboarding 
    /// where pending document shares are redeemed upon account creation.
    /// </summary>
    public class AccountController : Controller
    {
        private readonly SignInManager<IdentityUser<int>> _signInManager;
        private readonly UserManager<IdentityUser<int>> _userManager;
        private readonly FileMatrix_Pabiran_.Data.ApplicationDbContext _db;
        private readonly FileMatrix_Pabiran_.Services.EmailSenderService _emailSender;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            SignInManager<IdentityUser<int>> signInManager, 
            UserManager<IdentityUser<int>> userManager, 
            FileMatrix_Pabiran_.Data.ApplicationDbContext db,
            FileMatrix_Pabiran_.Services.EmailSenderService emailSender,
            IConfiguration configuration,
            ILogger<AccountController> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _db = db;
            _emailSender = emailSender;
            _configuration = configuration;
            _logger = logger;
        }

        private async Task<bool> ValidateRecaptcha(string? token)
        {
            if (string.IsNullOrEmpty(token)) return false;

            var secretKey = _configuration["Recaptcha:SecretKey"];
            using var client = new System.Net.Http.HttpClient();
            var response = await client.PostAsync($"https://www.google.com/recaptcha/api/siteverify?secret={secretKey}&response={token}", null);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var success = doc.RootElement.GetProperty("success").GetBoolean();
                if (!success) return false;

                // For v3, we check the score (0.0 - 1.0). 0.5 is the standard threshold for humans.
                if (doc.RootElement.TryGetProperty("score", out var scoreProp))
                {
                    return scoreProp.GetDouble() >= 0.5;
                }

                return true;
            }
            return false;
        }

        private const int MaxAuditActionChars = 50;

        /// <summary>
        /// Persists security events to <c>AuditLogs</c> (EntityType <c>Security</c>) for Super Admin reporting.
        /// Clears <see cref="AuditLog.UserID"/> if it does not exist in <c>Users</c> so inserts are not rejected by FK.
        /// </summary>
        private async Task LogAuditAsync(int? userId, string action, string details, int workplaceId = 0)
        {
            if (userId is int uid &&
                !await _db.Users.AsNoTracking().AnyAsync(u => u.UserID == uid))
            {
                userId = null;
            }

            var actionSafe = string.IsNullOrEmpty(action)
                ? "Security"
                : (action.Length <= MaxAuditActionChars ? action : action.Substring(0, MaxAuditActionChars));

            async Task<bool> TryInsertAsync(int? wpId)
            {
                var log = new AuditLog
                {
                    UserID = userId,
                    Action = actionSafe,
                    Details = details,
                    EntityID = userId ?? 0,
                    WorkplaceID = wpId,
                    PerformedAt = DateTime.UtcNow,
                    IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    EntityType = "Security"
                };
                _db.AuditLogs.Add(log);
                try
                {
                    await _db.SaveChangesAsync();
                    return true;
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogWarning(ex, "AuditLogs insert failed (WorkplaceID={WorkplaceId}) for {AuditAction}", wpId, action);
                    foreach (var entry in _db.ChangeTracker.Entries<AuditLog>().Where(e => e.State == EntityState.Added))
                        entry.State = EntityState.Detached;
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AuditLogs insert failed for action {AuditAction}", action);
                    foreach (var entry in _db.ChangeTracker.Entries<AuditLog>().Where(e => e.State == EntityState.Added))
                        entry.State = EntityState.Detached;
                    return false;
                }
            }

            int? primaryWp = workplaceId > 0 ? workplaceId : null;
            if (await TryInsertAsync(primaryWp))
                return;

            // Older databases (pre-migration) often require a non-null WorkplaceID on AuditLogs. Use any existing
            // workplace only as a storage anchor — Admin audit UI excludes EntityType "Security" so tenants do not see these.
            if (primaryWp == null)
            {
                var fallbackWp = await _db.Workplaces.AsNoTracking()
                    .OrderBy(w => w.WorkplaceID)
                    .Select(w => (int?)w.WorkplaceID)
                    .FirstOrDefaultAsync();
                if (fallbackWp.HasValue && await TryInsertAsync(fallbackWp.Value))
                    return;
            }

            _logger.LogError("AuditLogs: all insert attempts failed for action {AuditAction}", action);
        }

        /// <summary>
        /// Emails the account owner once when Identity lockout first triggers (avoids repeat mail while they keep trying).
        /// </summary>
        private async Task TrySendNewAccountLockoutNoticeAsync(
            IdentityUser<int> identityUser,
            bool wasAlreadyLockedBeforeThisAttempt,
            string failureContextDescription)
        {
            if (wasAlreadyLockedBeforeThisAttempt) return;
            if (!await _userManager.IsLockedOutAsync(identityUser)) return;

            var to = identityUser.Email;
            if (string.IsNullOrWhiteSpace(to)) return;

            var lockoutEnd = await _userManager.GetLockoutEndDateAsync(identityUser);
            var untilText = lockoutEnd.HasValue
                ? $"{lockoutEnd.Value.UtcDateTime:yyyy-MM-dd HH:mm} UTC"
                : "after the configured lockout period";

            try
            {
                var body = $@"
                <div style='font-family: sans-serif; max-width: 520px; margin: 0 auto; padding: 24px; border: 1px solid #fecaca; border-radius: 16px; background: #fef2f2;'>
                    <h2 style='color: #991b1b; margin: 0 0 12px;'>Suspicious activity — account temporarily locked</h2>
                    <p style='color: #475569; font-size: 15px; line-height: 1.6;'>Hello,</p>
                    <p style='color: #475569; font-size: 15px; line-height: 1.6;'>Your FileMatrix account was temporarily locked after <strong>{WebUtility.HtmlEncode(failureContextDescription)}</strong>.</p>
                    <p style='color: #1e293b; font-size: 15px; line-height: 1.6;'><strong>Lock ends (approx.):</strong> {untilText}</p>
                    <p style='color: #64748b; font-size: 14px; line-height: 1.6;'>If this was not you, change your password when you can sign in again. If you did not attempt to sign in, contact your administrator.</p>
                </div>";
                await _emailSender.SendAsync(to, "FileMatrix security alert — account locked", body);
            }
            catch (Exception ex)
            {
                Console.WriteLine("LOCKOUT NOTICE EMAIL ERROR: " + ex);
            }
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            // Redirect to the site root (landing page)
            // We intentionally send users to the landing page instead of a standalone login page.
            return RedirectToAction("Index", "Home");
        }

        /// <summary>
        /// Orchestrates the authentication flow. 
        /// FALLBACK: Implements a multi-stage lookup (Identity -> DMS Email -> DMS Username) 
        /// to ensure users can always log in with whatever identifier they remember.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            try
            {
                var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(Request.Form["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

                // CAPTCHA Validation
                var recaptchaResponse = Request.Form["g-recaptcha-response"];
                if (!await ValidateRecaptcha(recaptchaResponse))
                {
                    var msg = "Please complete the CAPTCHA verification.";

                    var captchaIdentifier = model.UsernameOrEmail?.Trim() ?? string.Empty;
                    var identityUserCaptcha = await _userManager.FindByEmailAsync(captchaIdentifier);
                    if (identityUserCaptcha == null)
                    {
                        var customUser = await _db.Users.FirstOrDefaultAsync(u => u.Username == captchaIdentifier);
                        if (customUser != null) identityUserCaptcha = await _userManager.FindByNameAsync(customUser.Username ?? "");
                    }
                    int? captchaUserId = null;
                    if (identityUserCaptcha != null && await _db.Users.AsNoTracking().AnyAsync(u => u.UserID == identityUserCaptcha.Id))
                        captchaUserId = identityUserCaptcha.Id;
                    await LogAuditAsync(captchaUserId, "CAPTCHA Failure", "Failed CAPTCHA verification at sign-in.");

                    ModelState.AddModelError(string.Empty, msg);
                    if (isAjax)
                    {
                        return Json(new { success = false, errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { { string.Empty, new[] { msg } } } });
                    }
                    return View(model);
                }

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

                if (!string.IsNullOrEmpty(identifier) && identifier.Contains('@'))
                {
                    var identityUser = await _userManager.FindByEmailAsync(identifier);

                    if (identityUser == null)
                    {
                        user = await _db.Users.FirstOrDefaultAsync(u =>
                            u.Email != null &&
                            u.Email.ToLower() == identifier.ToLower());
                    }
                    else
                    {
                        // Match by PK first — Identity and DMS share UserID for bridged accounts (avoids "User not found" on email casing drift).
                        user = await _db.Users.FirstOrDefaultAsync(u => u.UserID == identityUser.Id);
                        if (user == null && !string.IsNullOrEmpty(identityUser.Email))
                        {
                            user = await _db.Users.FirstOrDefaultAsync(u =>
                                u.Email != null &&
                                u.Email.ToLower() == identityUser.Email.ToLower());
                        }
                        if (user == null)
                            user = await EnsureDmsUserForIdentityLoginAsync(identityUser);
                    }
                }
                else
                {
                    user = await _db.Users.FirstOrDefaultAsync(u =>
                        u.Username != null &&
                        u.Username.ToLower() == identifier.ToLower());
                }

                if (user == null)
                {
                    await LogAuditAsync(null, "Login Failed", "Unknown username or email (no matching account).");

                    // map to the UsernameOrEmail field for inline display
                    ModelState.AddModelError(nameof(LoginViewModel.UsernameOrEmail), "User not found");
                    if (isAjax)
                    {
                        return Json(new { success = false, errors = new System.Collections.Generic.Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase) { { nameof(LoginViewModel.UsernameOrEmail), new[] { "User not found" } } } });
                    }
                    return View(model);
                }

                // Look up the IdentityUser for sign-in (PK first — same bridge as invite / DMS row)
                var identityUserForSignIn = await _userManager.FindByIdAsync(user.UserID.ToString());
                if (identityUserForSignIn == null)
                    identityUserForSignIn = await _userManager.FindByEmailAsync(user.Email ?? "");
                if (identityUserForSignIn == null)
                    identityUserForSignIn = await _userManager.FindByNameAsync(user.Username ?? "");
                if (identityUserForSignIn == null)
                {
                    await LogAuditAsync(user.UserID, "Login Failed", "DMS user exists but Identity account not found for sign-in.");
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    if (isAjax)
                    {
                        return Json(new { success = false, errors = new System.Collections.Generic.Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase) { { string.Empty, new[] { "Invalid login attempt." } } } });
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

                await FmSecurityClaims.EnsureLegacyEmailClaimAsync(_userManager, identityUserForSignIn);
                var signInClaims = await _userManager.GetClaimsAsync(identityUserForSignIn);
                bool useEmail2Fa = FmSecurityClaims.HasEmail2Fa(signInClaims);
                bool useAppMfa = FmSecurityClaims.HasAppMfa(signInClaims) && !string.IsNullOrEmpty(await _userManager.GetAuthenticatorKeyAsync(identityUserForSignIn));

                // Both email 2FA and app MFA: password first, then email code, then TOTP (session-staged; not a single Identity 2FA hop).
                if (useEmail2Fa && useAppMfa)
                {
                    var wasLockedBeforeDual = await _userManager.IsLockedOutAsync(identityUserForSignIn);
                    var dualCheck = await _signInManager.CheckPasswordSignInAsync(identityUserForSignIn, model.Password, lockoutOnFailure: true);

                    if (dualCheck.IsLockedOut)
                    {
                        var lockoutEndDual = await _userManager.GetLockoutEndDateAsync(identityUserForSignIn);
                        var lockoutMsgDual = lockoutEndDual.HasValue
                            ? $"Account locked until {lockoutEndDual.Value.UtcDateTime:yyyy-MM-dd HH:mm} UTC after 5 failed attempts."
                            : "Account locked after 5 failed attempts.";
                        await LogAuditAsync(user.UserID, "Login Locked", lockoutMsgDual);
                        await TrySendNewAccountLockoutNoticeAsync(identityUserForSignIn, wasLockedBeforeDual, "too many failed password attempts before your extra sign-in step");
                        var lockedErrorDual = "Your account has been temporarily locked due to too many failed login attempts. Please try again in 10 minutes.";
                        ModelState.AddModelError(nameof(LoginViewModel.Password), lockedErrorDual);
                        if (isAjax)
                            return Json(new { success = false, errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { { nameof(LoginViewModel.Password), new[] { lockedErrorDual } } } });
                        return View(model);
                    }

                    if (!dualCheck.Succeeded)
                    {
                        var failCountDual = await _userManager.GetAccessFailedCountAsync(identityUserForSignIn);
                        var attemptDetailDual = $"Wrong password. Attempt {failCountDual}/5 from IP {Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}.";
                        ModelState.AddModelError(nameof(LoginViewModel.Password), "Wrong password");
                        await LogAuditAsync(user.UserID, "Login Failed", attemptDetailDual);
                        if (isAjax)
                        {
                            var errorsByFieldDual = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [nameof(LoginViewModel.Password)] = new[] { "Wrong password" } };
                            return Json(new { success = false, errors = errorsByFieldDual });
                        }
                        return View(model);
                    }

                    var stagedToken = await _userManager.GenerateTwoFactorTokenAsync(identityUserForSignIn, MfaConstants.EmailTokenProvider);
                    await LogAuditAsync(user.UserID, "MFA staged (email step)", "Sign-in requires email code then authenticator.");
                    var stagedEmailBody = $@"
                        <div style='font-family: sans-serif; max-width: 500px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 16px; text-align: center;'>
                            <h2 style='color: #4f46e5; margin-bottom: 8px;'>Verify your identity — step 1 of 2</h2>
                            <p style='color: #64748b; font-size: 15px;'>Enter this code on the FileMatrix site, then you will be asked for your authenticator app.</p>
                            <div style='margin: 32px 0;'>
                                <span style='font-family: monospace; font-size: 32px; font-weight: 700; color: #1e1b4b; background: #f1f5f9; padding: 12px 24px; border-radius: 12px; letter-spacing: 4px;'>{stagedToken}</span>
                            </div>
                            <p style='font-size: 13px; color: #94a3b8;'>This code expires in 10 minutes.</p>
                        </div>";
                    await _emailSender.SendAsync(identityUserForSignIn.Email ?? string.Empty, "Your FileMatrix sign-in code (step 1)", stagedEmailBody);

                    HttpContext.Session.SetString("StagedMfa.UserId", identityUserForSignIn.Id.ToString());
                    HttpContext.Session.SetString("StagedMfa.RememberMe", model.RememberMe ? "1" : "0");
                    HttpContext.Session.Remove("StagedMfa.EmailOk");

                    if (isAjax)
                        return Json(new { success = true, redirectUrl = Url.Action(nameof(LoginStagedEmail), new { returnUrl }) });
                    return RedirectToAction(nameof(LoginStagedEmail), new { returnUrl });
                }

                var wasLockedBeforePassword = await _userManager.IsLockedOutAsync(identityUserForSignIn);
                var result = await _signInManager.PasswordSignInAsync(identityUserForSignIn, model.Password, model.RememberMe, lockoutOnFailure: true);
                
                if (result.RequiresTwoFactor)
                {
                    var loginClaims = await _userManager.GetClaimsAsync(identityUserForSignIn);
                    bool hasEmail2Fa = FmSecurityClaims.HasEmail2Fa(loginClaims);

                    if (hasEmail2Fa)
                    {
                        var token = await _userManager.GenerateTwoFactorTokenAsync(identityUserForSignIn, MfaConstants.EmailTokenProvider);
                        await LogAuditAsync(user.UserID, "MFA Requested", "Email sign-in code generated and sent.");
                        var emailBody = $@"
                        <div style='font-family: sans-serif; max-width: 500px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 16px; text-align: center;'>
                            <h2 style='color: #4f46e5; margin-bottom: 8px;'>Verify Your Identity</h2>
                            <p style='color: #64748b; font-size: 15px;'>Use the code below to complete your sign in to FileMatrix.</p>
                            <div style='margin: 32px 0;'>
                                <span style='font-family: monospace; font-size: 32px; font-weight: 700; color: #1e1b4b; background: #f1f5f9; padding: 12px 24px; border-radius: 12px; letter-spacing: 4px;'>{token}</span>
                            </div>
                            <p style='font-size: 13px; color: #94a3b8;'>This code will expire in 10 minutes.</p>
                            <hr style='border: 0; border-top: 1px solid #e2e8f0; margin: 24px 0;' />
                            <p style='font-size: 12px; color: #cbd5e1;'>If you didn't request this code, your account might be at risk. Change your password immediately.</p>
                        </div>";

                        await _emailSender.SendAsync(identityUserForSignIn.Email ?? string.Empty, "Your FileMatrix Login Code", emailBody);
                    }
                    else
                    {
                        await LogAuditAsync(user.UserID, "MFA Requested", "Authenticator code required (email 2FA is off for this account).");
                    }

                    if (isAjax)
                    {
                        return Json(new { success = true, redirectUrl = Url.Action("LoginWith2fa", new { rememberMe = model.RememberMe, returnUrl = returnUrl }) });
                    }
                    return RedirectToAction("LoginWith2fa", new { rememberMe = model.RememberMe, returnUrl = returnUrl });
                }

                if (result.Succeeded)
                {
                    return await CompleteLoginSuccessAsync(user, identityUserForSignIn, returnUrl, isAjax, "Login Success", "Standard password login successful.");
                }

                if (result.IsLockedOut)
                {
                    // Retrieve lockout end time for the audit message
                    var lockoutEnd = await _userManager.GetLockoutEndDateAsync(identityUserForSignIn);
                    var lockoutMsg = lockoutEnd.HasValue
                        ? $"Account locked until {lockoutEnd.Value.UtcDateTime:yyyy-MM-dd HH:mm} UTC after 5 failed attempts."
                        : "Account locked after 5 failed attempts.";

                    await LogAuditAsync(user.UserID, "Login Locked", lockoutMsg);

                    await TrySendNewAccountLockoutNoticeAsync(identityUserForSignIn, wasLockedBeforePassword, "too many failed password attempts");

                    var lockedError = "Your account has been temporarily locked due to too many failed login attempts. Please try again in 10 minutes.";
                    ModelState.AddModelError(nameof(LoginViewModel.Password), lockedError);
                    if (isAjax)
                    {
                        return Json(new { success = false, errors = new System.Collections.Generic.Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase) { { nameof(LoginViewModel.Password), new[] { lockedError } } } });
                    }
                    return View(model);
                }

                // Wrong password — audit for all accounts (Super Admin Login Attempts page filters EntityType Security).
                var failCount = await _userManager.GetAccessFailedCountAsync(identityUserForSignIn);
                var maxAttempts = 5;
                var attemptDetail = $"Wrong password. Attempt {failCount}/{maxAttempts} from IP {Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}.";
                ModelState.AddModelError(nameof(LoginViewModel.Password), "Wrong password");

                await LogAuditAsync(user.UserID, "Login Failed", attemptDetail);

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

        #region Staged sign-in (email 2FA + app MFA — password was already verified)

        [HttpGet]
        [AllowAnonymous]
        public IActionResult LoginStagedEmail(string? returnUrl = null)
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("StagedMfa.UserId")))
                return RedirectToAction("Index", "Home");
            ViewData["ReturnUrl"] = returnUrl;
            var emailSentFlag = TempData["TwoFactorEmailSent"];
            if (emailSentFlag is bool b && b)
                ViewData["EmailCodeSentNotice"] = true;
            else if (emailSentFlag?.ToString() == "True")
                ViewData["EmailCodeSentNotice"] = true;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginStagedEmail(string code, string? returnUrl = null)
        {
            var idStr = HttpContext.Session.GetString("StagedMfa.UserId");
            if (string.IsNullOrEmpty(idStr) || !int.TryParse(idStr, out _))
            {
                ClearStagedMfaSession();
                return RedirectToAction("Index", "Home");
            }

            var identityUser = await _userManager.FindByIdAsync(idStr);
            if (identityUser == null)
            {
                ClearStagedMfaSession();
                return RedirectToAction("Index", "Home");
            }

            ViewData["ReturnUrl"] = returnUrl;

            if (string.IsNullOrWhiteSpace(code))
            {
                ModelState.AddModelError(string.Empty, "Enter the code from your email.");
                return View();
            }

            var normalized = code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var ok = await _userManager.VerifyTwoFactorTokenAsync(identityUser, MfaConstants.EmailTokenProvider, normalized);
            if (!ok)
            {
                ModelState.AddModelError(string.Empty, "That code is invalid or has expired.");
                return View();
            }

            HttpContext.Session.SetString("StagedMfa.EmailOk", "1");

            var claims = await _userManager.GetClaimsAsync(identityUser);
            var key = await _userManager.GetAuthenticatorKeyAsync(identityUser);
            if (FmSecurityClaims.HasAppMfa(claims) && !string.IsNullOrEmpty(key))
                return RedirectToAction(nameof(LoginStagedAuthenticator), new { returnUrl });

            var rememberMe = string.Equals(HttpContext.Session.GetString("StagedMfa.RememberMe"), "1", StringComparison.Ordinal);
            await _signInManager.SignInAsync(identityUser, isPersistent: rememberMe);
            ClearStagedMfaSession();

            var dmsUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == identityUser.Email);
            if (dmsUser == null) return RedirectToAction("Index", "Organizations");
            return await CompleteLoginSuccessAsync(dmsUser, identityUser, returnUrl, false, "MFA Success", "Email sign-in code verified.");
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendLoginStagedEmail(string? returnUrl = null)
        {
            var idStr = HttpContext.Session.GetString("StagedMfa.UserId");
            if (string.IsNullOrEmpty(idStr)) return RedirectToAction("Index", "Home");
            var identityUser = await _userManager.FindByIdAsync(idStr);
            if (identityUser == null) { ClearStagedMfaSession(); return RedirectToAction("Index", "Home"); }

            var token = await _userManager.GenerateTwoFactorTokenAsync(identityUser, MfaConstants.EmailTokenProvider);
            var emailBody = $@"
                <div style='font-family: sans-serif; max-width: 500px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 16px; text-align: center;'>
                    <h2 style='color: #4f46e5; margin-bottom: 8px;'>Verify Your Identity</h2>
                    <p style='color: #64748b; font-size: 15px;'>Use the code below for step 1 of your FileMatrix sign-in.</p>
                    <div style='margin: 32px 0;'>
                        <span style='font-family: monospace; font-size: 32px; font-weight: 700; color: #1e1b4b; background: #f1f5f9; padding: 12px 24px; border-radius: 12px; letter-spacing: 4px;'>{token}</span>
                    </div>
                    <p style='font-size: 13px; color: #94a3b8;'>This code will expire in 10 minutes.</p>
                </div>";
            await _emailSender.SendAsync(identityUser.Email ?? string.Empty, "Your FileMatrix sign-in code (step 1)", emailBody);
            TempData["TwoFactorEmailSent"] = true;
            return RedirectToAction(nameof(LoginStagedEmail), new { returnUrl });
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult LoginStagedAuthenticator(string? returnUrl = null)
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("StagedMfa.UserId"))
                || !string.Equals(HttpContext.Session.GetString("StagedMfa.EmailOk"), "1", StringComparison.Ordinal))
                return RedirectToAction("Index", "Home");
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginStagedAuthenticator(string code, string? returnUrl = null)
        {
            var idStr = HttpContext.Session.GetString("StagedMfa.UserId");
            if (string.IsNullOrEmpty(idStr)
                || !string.Equals(HttpContext.Session.GetString("StagedMfa.EmailOk"), "1", StringComparison.Ordinal))
            {
                ClearStagedMfaSession();
                return RedirectToAction("Index", "Home");
            }

            var identityUser = await _userManager.FindByIdAsync(idStr);
            if (identityUser == null)
            {
                ClearStagedMfaSession();
                return RedirectToAction("Index", "Home");
            }

            ViewData["ReturnUrl"] = returnUrl;

            if (string.IsNullOrWhiteSpace(code))
            {
                ModelState.AddModelError(string.Empty, "Enter the code from your authenticator app.");
                return View();
            }

            var normalized = code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var authProv = _userManager.Options.Tokens.AuthenticatorTokenProvider;
            var ok = await _userManager.VerifyTwoFactorTokenAsync(identityUser, authProv, normalized);
            if (!ok)
            {
                ModelState.AddModelError(string.Empty, "That code does not match your authenticator app.");
                return View();
            }

            var rememberMe = string.Equals(HttpContext.Session.GetString("StagedMfa.RememberMe"), "1", StringComparison.Ordinal);
            await _signInManager.SignInAsync(identityUser, isPersistent: rememberMe);
            ClearStagedMfaSession();

            var dmsUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == identityUser.Email);
            if (dmsUser == null) return RedirectToAction("Index", "Organizations");
            return await CompleteLoginSuccessAsync(dmsUser, identityUser, returnUrl, false, "MFA Success", "Email and authenticator verification completed.");
        }

        #endregion

        [HttpGet]
        public async Task<IActionResult> LoginWith2fa(bool rememberMe, string? returnUrl = null)
        {
            // Ensure the user has gone through the username & password screen first
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();

            if (user == null)
            {
                return RedirectToAction("Index", "Home");
            }

            ViewData["ReturnUrl"] = returnUrl;
            ViewData["RememberMe"] = rememberMe;

            var authKey = await _userManager.GetAuthenticatorKeyAsync(user);
            var lc = await _userManager.GetClaimsAsync(user);
            ViewData["HasAuthenticator"] = FmSecurityClaims.HasAppMfa(lc) && !string.IsNullOrEmpty(authKey);
            var emailSentFlag = TempData["TwoFactorEmailSent"];
            if (emailSentFlag is bool b && b)
            {
                ViewData["EmailCodeSentNotice"] = true;
            }
            else if (emailSentFlag?.ToString() == "True")
            {
                ViewData["EmailCodeSentNotice"] = true;
            }

            return View();
        }

        /// <summary>
        /// Sends (or re-sends) an email login code while the user is in the pending two-factor state.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendLoginTwoFactorEmail(bool rememberMe, bool rememberMachine, string? returnUrl = null)
        {
            var identityUser = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (identityUser == null) return RedirectToAction("Index", "Home");

            var token = await _userManager.GenerateTwoFactorTokenAsync(identityUser, MfaConstants.EmailTokenProvider);
            var emailBody = $@"
                <div style='font-family: sans-serif; max-width: 500px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 16px; text-align: center;'>
                    <h2 style='color: #4f46e5; margin-bottom: 8px;'>Verify Your Identity</h2>
                    <p style='color: #64748b; font-size: 15px;'>Use the code below to complete your sign in to FileMatrix.</p>
                    <div style='margin: 32px 0;'>
                        <span style='font-family: monospace; font-size: 32px; font-weight: 700; color: #1e1b4b; background: #f1f5f9; padding: 12px 24px; border-radius: 12px; letter-spacing: 4px;'>{token}</span>
                    </div>
                    <p style='font-size: 13px; color: #94a3b8;'>This code will expire in 10 minutes.</p>
                </div>";
            await _emailSender.SendAsync(identityUser.Email ?? string.Empty, "Your FileMatrix Login Code", emailBody);
            TempData["TwoFactorEmailSent"] = true;
            return RedirectToAction(nameof(LoginWith2fa), new { rememberMe, returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginWith2fa(string code, bool rememberMe, bool rememberMachine, string? returnUrl = null)
        {
            if (string.IsNullOrEmpty(code))
            {
                ModelState.AddModelError(string.Empty, "Please enter the verification code.");
                return await LoginWith2fa(rememberMe, returnUrl);
            }

            // Must get user BEFORE sign in as the 2FA session ends upon success
            var identityUser = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (identityUser == null) return RedirectToAction("Index", "Home");

            var normalized = code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var authenticatorProvider = _userManager.Options.Tokens.AuthenticatorTokenProvider;
            var loginClaims = await _userManager.GetClaimsAsync(identityUser);
            bool hasAppMfaClaim = FmSecurityClaims.HasAppMfa(loginClaims);
            bool hasEmail2FaClaim = FmSecurityClaims.HasEmail2Fa(loginClaims);
            bool hasAuthenticatorKey = !string.IsNullOrEmpty(await _userManager.GetAuthenticatorKeyAsync(identityUser));

            var wasLockedBeforeMfa = await _userManager.IsLockedOutAsync(identityUser);

            Microsoft.AspNetCore.Identity.SignInResult result;
            if (hasEmail2FaClaim && !hasAppMfaClaim)
            {
                // Email 2FA only — single Identity second factor.
                result = await _signInManager.TwoFactorSignInAsync(MfaConstants.EmailTokenProvider, normalized, rememberMe, rememberMachine);
            }
            else if (!hasEmail2FaClaim && hasAppMfaClaim && hasAuthenticatorKey)
            {
                // Legacy: app MFA without email claim — app only.
                result = await _signInManager.TwoFactorSignInAsync(authenticatorProvider, normalized, rememberMe, rememberMachine);
            }
            else if (hasEmail2FaClaim && hasAppMfaClaim)
            {
                // Both enabled accounts should use staged login; if we land here, accept email only to avoid lock-out.
                result = await _signInManager.TwoFactorSignInAsync(MfaConstants.EmailTokenProvider, normalized, rememberMe, rememberMachine);
            }
            else
            {
                if (hasAuthenticatorKey)
                {
                    result = await _signInManager.TwoFactorSignInAsync(authenticatorProvider, normalized, rememberMe, rememberMachine);
                    if (!result.Succeeded)
                        result = await _signInManager.TwoFactorSignInAsync(MfaConstants.EmailTokenProvider, normalized, rememberMe, rememberMachine);
                }
                else
                {
                    result = await _signInManager.TwoFactorSignInAsync(MfaConstants.EmailTokenProvider, normalized, rememberMe, rememberMachine);
                }
            }

            if (result.Succeeded)
            {
                var dmsUserObj = await _db.Users.FirstOrDefaultAsync(u => u.Email == identityUser.Email);
                if (dmsUserObj == null) return RedirectToAction("Index", "Organizations");
                return await CompleteLoginSuccessAsync(dmsUserObj, identityUser, returnUrl, false, "MFA Success", "Second factor verified.");
            }

            if (result.IsLockedOut)
            {
                if (identityUser != null)
                {
                    var dmsUserObj = await _db.Users.FirstOrDefaultAsync(u => u.Email == identityUser.Email);
                    await LogAuditAsync(dmsUserObj?.UserID, "Account Locked", "User locked out during MFA verification.");
                    await TrySendNewAccountLockoutNoticeAsync(identityUser, wasLockedBeforeMfa, "too many failed verification codes during sign-in");
                }
                ModelState.AddModelError(string.Empty, "Account locked out due to too many failed attempts.");
                await PopulateLoginWith2faViewDataAsync(rememberMe, returnUrl);
                return View();
            }

            if (identityUser != null)
            {
                var dmsUserObj = await _db.Users.FirstOrDefaultAsync(u => u.Email == identityUser.Email);
                await LogAuditAsync(dmsUserObj?.UserID, "MFA Failed", "Invalid MFA code entered.");
            }
            ModelState.AddModelError(string.Empty, "Invalid verification code.");
            await PopulateLoginWith2faViewDataAsync(rememberMe, returnUrl);
            return View();
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

        /// <summary>
        /// Handles new user registration, including Identity record creation, 
        /// DMS profile syncing, and email verification delivery.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            try
            {
                var isAjaxCheck = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(Request.Form["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

                // CAPTCHA Validation
                var recaptchaResponse = Request.Form["g-recaptcha-response"];
                if (!await ValidateRecaptcha(recaptchaResponse))
                {
                    var msg = "Please complete the CAPTCHA verification.";
                    ModelState.AddModelError(string.Empty, msg);
                    if (isAjaxCheck)
                    {
                        return Json(new { success = false, errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { { string.Empty, new[] { msg } } } });
                    }
                    return View(model);
                }

                if (!ModelState.IsValid)
                {
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
                    isAjaxCheck = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
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

        /// <summary>
        /// Context Switcher: Updates the user's 'LastWorkplaceID' cookie to change 
        /// their active organizational context.
        /// </summary>
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


        // ─────────────────────────────────────────────────────────────
        // FORGOT / RESET PASSWORD
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Step 1 – Show the "enter your email" form.
        /// </summary>
        [HttpGet]
        public IActionResult ForgotPassword() => View();

        /// <summary>
        /// Step 2 – Generate a password-reset token and email the link.
        /// Always shows the same success message to prevent user enumeration.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                ModelState.AddModelError(string.Empty, "Please enter your email address.");
                return View();
            }

            // Always show the confirmation page regardless of whether the email exists
            // to prevent user enumeration attacks.
            var identityUser = await _userManager.FindByEmailAsync(email.Trim());
            if (identityUser != null)
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(identityUser);
                var resetUrl = Url.Action(
                    "ResetPassword", "Account",
                    new { token = token, email = email.Trim() },
                    protocol: Request.Scheme
                );

                var body = $@"
                    <div style='font-family:sans-serif;max-width:600px;margin:0 auto;padding:24px;border:1px solid #e2e8f0;border-radius:16px;'>
                        <h2 style='color:#4f46e5;margin-bottom:8px;'>Reset your password</h2>
                        <p style='color:#475569;'>We received a request to reset the password for your FileMatrix account.</p>
                        <p style='color:#475569;'>Click the button below to choose a new password. This link expires in <strong>2 hours</strong>.</p>
                        <div style='margin:32px 0;text-align:center;'>
                            <a href='{resetUrl}' style='background:#4f46e5;color:#fff;padding:12px 28px;border-radius:8px;text-decoration:none;font-weight:600;display:inline-block;'>
                                Reset Password
                            </a>
                        </div>
                        <p style='font-size:13px;color:#94a3b8;'>If you did not request this, you can safely ignore this email — your password will not change.</p>
                        <hr style='border:0;border-top:1px solid #e2e8f0;margin:24px 0;'/>
                        <p style='font-size:12px;color:#cbd5e1;'>FileMatrix · Secure Document Management</p>
                    </div>";

                try
                {
                    await _emailSender.SendAsync(email.Trim(), "Reset your FileMatrix password", body);
                }
                catch
                {
                    // Silently swallow so as not to reveal account existence on send failure.
                }
            }

            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        [HttpGet]
        public IActionResult ForgotPasswordConfirmation() => View();

        /// <summary>
        /// Step 3 – Show the "enter new password" form (token comes from the email link).
        /// </summary>
        [HttpGet]
        public IActionResult ResetPassword(string? token, string? email)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(email))
                return RedirectToAction("Index", "Home");

            ViewBag.Token = token;
            ViewBag.Email = email;
            return View();
        }

        /// <summary>
        /// Step 4 – Validate token and apply the new password.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string email, string token, string newPassword, string confirmPassword)
        {
            if (newPassword != confirmPassword)
            {
                ModelState.AddModelError(string.Empty, "Passwords do not match.");
                ViewBag.Token = token;
                ViewBag.Email = email;
                return View();
            }

            var identityUser = await _userManager.FindByEmailAsync(email);
            if (identityUser == null)
            {
                // Don't reveal that the user doesn't exist.
                return RedirectToAction(nameof(ResetPasswordConfirmation));
            }

            var result = await _userManager.ResetPasswordAsync(identityUser, token, newPassword);
            if (result.Succeeded)
            {
                return RedirectToAction(nameof(ResetPasswordConfirmation));
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            ViewBag.Token = token;
            ViewBag.Email = email;
            return View();
        }

        [HttpGet]
        public IActionResult ResetPasswordConfirmation() => View();

        private void ClearStagedMfaSession()
        {
            HttpContext.Session.Remove("StagedMfa.UserId");
            HttpContext.Session.Remove("StagedMfa.RememberMe");
            HttpContext.Session.Remove("StagedMfa.EmailOk");
        }

        /// <summary>
        /// Creates a missing DMS Users row for an Identity account (e.g. Super Admin invite) so login can resolve the profile.
        /// </summary>
        private async Task<User?> EnsureDmsUserForIdentityLoginAsync(IdentityUser<int> identityUser)
        {
            var existing = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserID == identityUser.Id);
            if (existing != null)
                return await _db.Users.FindAsync(identityUser.Id);

            var canonicalEmail = identityUser.Email;
            if (string.IsNullOrWhiteSpace(canonicalEmail))
                canonicalEmail = identityUser.UserName;
            if (string.IsNullOrWhiteSpace(canonicalEmail))
                return null;

            var userName = identityUser.UserName ?? canonicalEmail;
            var roles = await _userManager.GetRolesAsync(identityUser);
            var roleCode = roles.Any(r => string.Equals(r, "SuperAdmin", StringComparison.OrdinalIgnoreCase)) ? 0 : 3;
            var display = canonicalEmail.Contains('@', StringComparison.Ordinal)
                ? canonicalEmail.Split('@')[0]
                : canonicalEmail;

            var dms = new User
            {
                UserID = identityUser.Id,
                Username = userName,
                Email = canonicalEmail,
                PasswordHash = identityUser.PasswordHash ?? "",
                DisplayName = display,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Role = roleCode
            };

            try
            {
                _db.Users.Add(dms);
                await _db.SaveChangesAsync();
                return dms;
            }
            catch
            {
                return await _db.Users.FirstOrDefaultAsync(u => u.UserID == identityUser.Id);
            }
        }

        /// <summary>
        /// Shared post-auth redirect: last login, invite redemption, org routing.
        /// </summary>
        private async Task<IActionResult> CompleteLoginSuccessAsync(User user, IdentityUser<int> identityUser, string? returnUrl, bool isAjax, string auditAction, string auditDetails)
        {
            await LogAuditAsync(user.UserID, auditAction, auditDetails, 0);
            user.LastLogin = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            try
            {
                var uid = user.UserID;
                var exists = await _db.Users.AnyAsync(u => u.UserID == uid);
                if (!exists)
                {
                    _db.Users.Add(new User
                    {
                        UserID = uid,
                        Username = user.Username,
                        Email = user.Email,
                        DisplayName = user.Username ?? user.Email,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync();
                }
            }
            catch { }

            bool hasActiveOrg = false;
            try
            {
                hasActiveOrg = await _db.WorkplaceMembers.AnyAsync(m => m.UserID == user.UserID);
                if (!hasActiveOrg)
                    hasActiveOrg = await _db.Workplaces.AnyAsync(o => o.CreatedByUserID == user.UserID && o.IsActive);
            }
            catch { hasActiveOrg = false; }

            var defaultRedirect = hasActiveOrg ? Url.Action("Index", "Admin", new { area = "Admin" }) : Url.Action("Index", "Organizations");
            if (await _userManager.IsInRoleAsync(identityUser, "SuperAdmin"))
                defaultRedirect = Url.Action("Index", "SuperAdmin", new { area = "SuperAdmin" });

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
                var redirect = docShareRedirect ?? (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : defaultRedirect);
                return Json(new { success = true, redirectUrl = redirect });
            }

            if (docShareRedirect != null) return Redirect(docShareRedirect);
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return Redirect(defaultRedirect);
        }

        /// <summary>
        /// Fills view metadata when the second-factor screen is shown again after a failed attempt.
        /// </summary>
        private async Task PopulateLoginWith2faViewDataAsync(bool rememberMe, string? returnUrl)
        {
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            ViewData["ReturnUrl"] = returnUrl;
            ViewData["RememberMe"] = rememberMe;
            if (user != null)
            {
                var lc = await _userManager.GetClaimsAsync(user);
                var authKey = await _userManager.GetAuthenticatorKeyAsync(user);
                ViewData["HasAuthenticator"] = FmSecurityClaims.HasAppMfa(lc) && !string.IsNullOrEmpty(authKey);
            }
            else
            {
                ViewData["HasAuthenticator"] = false;
            }
        }
    }
}
