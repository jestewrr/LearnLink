using System.Security.Claims;

namespace LearnLink.Services
{
    /// <summary>
    /// Provides the current school context for multi-tenancy.
    /// When CurrentSchoolId is null, the user is a platform-level SuperAdmin (sees all schools).
    /// </summary>
    public interface ISchoolContext
    {
        int? CurrentSchoolId { get; }
        string? CurrentSchoolName { get; }
        bool IsPlatformAdmin { get; }
    }

    public class HttpSchoolContext : ISchoolContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpSchoolContext(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public int? CurrentSchoolId
        {
            get
            {
                // Check for school switcher override (SuperAdmin switching context)
                var switchedSchoolId = _httpContextAccessor.HttpContext?.Session?.GetInt32("SwitchedSchoolId");
                if (switchedSchoolId.HasValue && switchedSchoolId.Value > 0)
                    return switchedSchoolId.Value;

                var claim = _httpContextAccessor.HttpContext?.User?.FindFirst("SchoolId");
                if (claim != null && int.TryParse(claim.Value, out var schoolId) && schoolId > 0)
                    return schoolId;

                return null; // Platform admin or unauthenticated
            }
        }

        public string? CurrentSchoolName
        {
            get
            {
                return _httpContextAccessor.HttpContext?.User?.FindFirst("SchoolName")?.Value;
            }
        }

        public bool IsPlatformAdmin
        {
            get
            {
                var user = _httpContextAccessor.HttpContext?.User;
                if (user == null || !user.Identity?.IsAuthenticated == true)
                    return false;

                // A platform admin has no SchoolId claim (or it's empty) and is SuperAdmin
                var schoolClaim = user.FindFirst("SchoolId");
                var hasNoSchool = schoolClaim == null || string.IsNullOrEmpty(schoolClaim.Value) || schoolClaim.Value == "0";
                var isSuperAdmin = user.IsInRole("SuperAdmin");

                return hasNoSchool && isSuperAdmin;
            }
        }
    }
}
