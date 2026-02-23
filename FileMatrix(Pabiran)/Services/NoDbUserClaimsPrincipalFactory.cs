using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using FileMatrix_Pabiran_.Models;

namespace FileMatrix_Pabiran_.Services
{
    // Build a ClaimsPrincipal for a user without querying a separate user-claims table.
    // This avoids SQL queries against missing AspNetUserClaims/AspNetRoleClaims tables
    // when your project stores roles/users in custom tables (Users, Roles, UserRoles).
    public class NoDbUserClaimsPrincipalFactory : IUserClaimsPrincipalFactory<User>
    {
        private readonly UserManager<User> _userManager;

        public NoDbUserClaimsPrincipalFactory(UserManager<User> userManager)
        {
            _userManager = userManager;
        }

        public async Task<ClaimsPrincipal> CreateAsync(User user)
        {
            var identity = new ClaimsIdentity("Identity.Application");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()));
            if (!string.IsNullOrEmpty(user.Username))
                identity.AddClaim(new Claim(ClaimTypes.Name, user.Username));
            if (!string.IsNullOrEmpty(user.Email))
                identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));

            // Add role claims using UserManager (this will use your UserRoles mapping)
            try
            {
                var roles = await _userManager.GetRolesAsync(user);
                foreach (var role in roles)
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                }
            }
            catch
            {
                // If role resolution fails, ignore — this factory is defensive for missing tables
            }

            return new ClaimsPrincipal(identity);
        }
    }
}
