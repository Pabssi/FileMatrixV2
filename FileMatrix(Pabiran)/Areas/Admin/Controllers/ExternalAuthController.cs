using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using FileMatrix_Pabiran_.Data;
using FileMatrix_Pabiran_.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace FileMatrix_Pabiran_.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize]
    public class ExternalAuthController : BaseAdminController
    {
        private readonly IConfiguration _config;

        public ExternalAuthController(ApplicationDbContext context, IConfiguration config) : base(context)
        {
            _config = config;
        }

        [HttpGet]
        public IActionResult ConnectGoogleDrive()
        {
            var clientId = _config["GoogleAuth:ClientId"];
            var redirectUri = Url.Action("GoogleCallback", "ExternalAuth", new { area = "Admin" }, Request.Scheme);

            var scopes = new[] { DriveService.Scope.DriveFile };
            var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                          $"client_id={clientId}&" +
                          $"redirect_uri={redirectUri}&" +
                          $"response_type=code&" +
                          $"scope={string.Join(" ", scopes)}&" +
                          $"access_type=offline&" +
                          $"prompt=consent";

            return Redirect(authUrl);
        }

        [HttpGet]
        public async Task<IActionResult> GoogleCallback(string code)
        {
            if (string.IsNullOrEmpty(code)) return BadRequest("Authorization code is missing.");

            var clientId = _config["GoogleAuth:ClientId"];
            var clientSecret = _config["GoogleAuth:ClientSecret"];
            var redirectUri = Url.Action("GoogleCallback", "ExternalAuth", new { area = "Admin" }, Request.Scheme);

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret }
            });

            var tokenResponse = await flow.ExchangeCodeForTokenAsync("user", code, redirectUri, CancellationToken.None);

            if (tokenResponse != null && CurrentWorkplace != null)
            {
                var workplace = await _context.Workplaces.FindAsync(CurrentWorkplace.WorkplaceID);
                if (workplace != null)
                {
                    workplace.GoogleDriveAccessToken = tokenResponse.AccessToken;
                    workplace.GoogleDriveRefreshToken = tokenResponse.RefreshToken;
                    workplace.GoogleDriveTokenExpiry = DateTime.UtcNow.AddSeconds((double)(tokenResponse.ExpiresInSeconds ?? 3600));
                    
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Google Drive successfully connected!";
                }
            }

            return RedirectToAction("Index", "Settings");
        }
    }
}
