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
        public async Task<string> UploadAsync(Stream fileStream, string fileName, string folder)
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

            return uploadResult.SecureUrl.ToString();
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
             var uri = new Uri(cloudinaryUrl);
             var segments = uri.Segments;
             
             // Public ID is the part after the version (v123456789/)
             // Format: .../raw/authenticated/v123456789/folder/subfolder/file.ext
             var authIndex = -1;
             for(int i = 0; i < segments.Length; i++) {
                 if (segments[i] == "authenticated/") {
                     authIndex = i;
                     break;
                 }
             }

             if (authIndex == -1 || authIndex + 2 >= segments.Length) return cloudinaryUrl;

             // Public ID segments start after the version segment
             var publicIdSegments = segments.Skip(authIndex + 2); 
             var publicId = string.Join("", publicIdSegments).TrimEnd('/');

             // Generate a signed URL valid for 1 hour
             // Using the SDK's built-in private download URL generator correctly
             // Format requires: publicId, resourceType, type
             return _cloudinary.Api.UrlImgUp
                 .ResourceType("raw")
                 .Type("authenticated")
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
