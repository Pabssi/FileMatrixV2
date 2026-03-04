using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;
using FileMatrix_Pabiran_.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileMatrix_Pabiran_.Data;

namespace FileMatrix_Pabiran_.Services
{
    public class GoogleDriveService
    {
        private readonly IConfiguration _config;
        private readonly ApplicationDbContext _context;

        public GoogleDriveService(IConfiguration config, ApplicationDbContext context)
        {
            _config = config;
            _context = context;
        }

        private async Task<DriveService> GetDriveServiceAsync(Workplace workplace)
        {
            var clientId = _config["GoogleAuth:ClientId"];
            var clientSecret = _config["GoogleAuth:ClientSecret"];

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret }
            });

            var token = new TokenResponse
            {
                AccessToken = workplace.GoogleDriveAccessToken,
                RefreshToken = workplace.GoogleDriveRefreshToken,
                IssuedUtc = DateTime.UtcNow.AddHours(-1) // Estimate
            };

            var userCredential = new UserCredential(flow, "user", token);

            // Refresh token if expired
            if (workplace.GoogleDriveTokenExpiry < DateTime.UtcNow)
            {
                await userCredential.RefreshTokenAsync(CancellationToken.None);
                
                // Save new tokens
                workplace.GoogleDriveAccessToken = userCredential.Token.AccessToken;
                if (!string.IsNullOrEmpty(userCredential.Token.RefreshToken))
                {
                    workplace.GoogleDriveRefreshToken = userCredential.Token.RefreshToken;
                }
                workplace.GoogleDriveTokenExpiry = DateTime.UtcNow.AddSeconds((double)(userCredential.Token.ExpiresInSeconds ?? 3600));
                
                _context.Workplaces.Update(workplace);
                await _context.SaveChangesAsync();
            }

            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = userCredential,
                ApplicationName = "FileMatrix"
            });
        }

        public async Task<(string? FileId, string? WebViewLink)> UploadFileAsync(Workplace workplace, string fileName, string contentType, Stream fileStream)
        {
            if (string.IsNullOrEmpty(workplace.GoogleDriveRefreshToken))
            {
                return (null, null);
            }

            var service = await GetDriveServiceAsync(workplace);

            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = fileName,
                Parents = string.IsNullOrEmpty(workplace.GoogleBackupFolderID) ? null : new[] { workplace.GoogleBackupFolderID }
            };

            FilesResource.CreateMediaUpload request;
            request = service.Files.Create(fileMetadata, fileStream, contentType);
            request.Fields = "id, webViewLink";
            
            await request.UploadAsync();

            var file = request.ResponseBody;
            return (file?.Id, file?.WebViewLink);
        }
    }
}
