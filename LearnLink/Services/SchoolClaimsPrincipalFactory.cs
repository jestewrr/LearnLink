using LearnLink.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace LearnLink.Services
{
    /// <summary>
    /// Custom claims principal factory that adds SchoolId and SchoolName claims
    /// to the user's identity on login. This ensures school context is available
    /// in every request without querying the database each time.
    /// </summary>
    public class SchoolClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
    {
        private readonly Data.ApplicationDbContext _context;

        public SchoolClaimsPrincipalFactory(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IOptions<IdentityOptions> options,
            Data.ApplicationDbContext context)
            : base(userManager, roleManager, options)
        {
            _context = context;
        }

        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
        {
            var identity = await base.GenerateClaimsAsync(user);

            // Add school claims
            if (user.SchoolId.HasValue && user.SchoolId.Value > 0)
            {
                identity.AddClaim(new Claim("SchoolId", user.SchoolId.Value.ToString()));

                var school = await _context.Schools.FindAsync(user.SchoolId.Value);
                if (school != null)
                {
                    identity.AddClaim(new Claim("SchoolName", school.Name));
                    identity.AddClaim(new Claim("SchoolCode", school.Code));
                }
            }

            return identity;
        }
    }
}
