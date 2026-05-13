using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using FileMatrix_Pabiran_.Models;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FileMatrix_Pabiran_.Services
{
    public class CloudinaryUploadResult
    {
        public string? SecureUrl { get; set; }
        public string? PublicId { get; set; }
    }

    public class CloudinaryService
    {
        private readonly Cloudinary _cloudinary;

        public CloudinaryService(IOptions<CloudinarySettings> config)
        {
            var acc = new Account(
                config.Value.CloudName,
                config.Value.ApiKey,
                config.Value.ApiSecret
            );
            _cloudinary = new Cloudinary(acc);
        }

        /// <summary>
        /// Uploads a file to Cloudinary with 'authenticated' access mode.
        /// Authenticated files are NOT public and require a signed URL to access.
        /// </summary>
        public async Task<CloudinaryUploadResult> UploadAsync(Stream fileStream, string fileName, string folder)
        {
            var uploadParams = new RawUploadParams()
            {
                File = new FileDescription(fileName, fileStream),
                Folder = $"filematrix/{folder}",
                Type = "authenticated" // Secure: requires signature for access
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            if (uploadResult.Error != null)
            {
                throw new Exception($"Cloudinary Upload Error: {uploadResult.Error.Message}");
            }

            return new CloudinaryUploadResult
            {
                SecureUrl = uploadResult.SecureUrl.ToString(),
                PublicId = uploadResult.PublicId
            };
        }

        /// <summary>
        /// Generates a time-limited signed URL for a private/authenticated Cloudinary resource.
        /// Falls back to the original URL if the resource is public (legacy).
        /// </summary>
        public string GetSignedUrl(string cloudinaryUrl)
        {
            if (string.IsNullOrEmpty(cloudinaryUrl)) return string.Empty;

            // If it's already an authenticated URL, we MUST generate a signature
            if (cloudinaryUrl.Contains("/authenticated/"))
            {
                try 
                {
                    return GetSecureDownloadUrl(cloudinaryUrl);
                }
                catch
                {
                    return cloudinaryUrl;
                }
            }

            // Legacy support: If it's a standard 'upload' URL, it's public.
            // We return it as-is, but new uploads will use 'authenticated'.
            return cloudinaryUrl;
        }

        /// <summary>
        /// Uses the Cloudinary SDK to generate a signed download URL for authenticated resources.
        /// </summary>
        private string GetSecureDownloadUrl(string cloudinaryUrl)
        {
            // Extract the PublicID from the URL
            // Format: https://res.cloudinary.com/cloud/raw/authenticated/v12345/folder/file.ext
            var uri = new Uri(cloudinaryUrl);
            var segments = uri.Segments;
            
            var authIndex = Array.FindIndex(segments, s => s == "authenticated/");
            if (authIndex == -1 || authIndex + 2 >= segments.Length) return cloudinaryUrl;

            // Public ID includes everything after the version segment
            var publicId = string.Join("", segments.Skip(authIndex + 2)).TrimEnd('/');

            // Use the SDK's built-in URL builder for authenticated 'raw' resources
            return _cloudinary.Api.Url
                .ResourceType("raw")
                .Action("authenticated")
                .Secure(true)
                .Signed(true)
                .BuildUrl(publicId);
        }

        /// <summary>
        /// Deletes a file from Cloudinary.
        /// </summary>
        /// <param name="publicId">The public ID of the file in Cloudinary.</param>
        public async Task<bool> DeleteAsync(string publicId)
        {
            var deletionParams = new DeletionParams(publicId);
            var result = await _cloudinary.DestroyAsync(deletionParams);
            return result.Result == "ok";
        }
    }
}
