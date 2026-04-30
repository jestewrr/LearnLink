using LearnLink.Data;
using LearnLink.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LearnLink.Services;

// ==================== Configuration ====================

public class LoginSecuritySettings
{
    /// <summary>Number of consecutive failures before CAPTCHA is required (default 3).</summary>
    public int CaptchaThreshold { get; set; } = 3;

    /// <summary>Number of consecutive failures before the account is locked (default 7).</summary>
    public int LockoutThreshold { get; set; } = 7;

    /// <summary>Duration (minutes) the account stays locked after reaching LockoutThreshold.</summary>
    public int LockoutDurationMinutes { get; set; } = 30;

    /// <summary>Lifetime (days) for the trusted-device cookie.</summary>
    public int TrustedDeviceCookieDays { get; set; } = 180;

    /// <summary>Maximum password-reset requests allowed per hour per account.</summary>
    public int MaxPasswordResetsPerHour { get; set; } = 3;

    /// <summary>Days to retain login attempt audit records (0 = no purge).</summary>
    public int AuditLogRetentionDays { get; set; } = 90;

    /// <summary>Send email to the user when their account is locked.</summary>
    public bool SendLockoutEmail { get; set; } = true;
}

// ==================== Result Enums ====================

public enum LoginFailureAction
{
    None,
    ShowCaptchaWarning,
    LockAccount
}

// ==================== Interface ====================

public interface ILoginSecurityService
{
    /// <summary>Determines whether CAPTCHA should be presented on the login form.</summary>
    Task<bool> ShouldRequireCaptchaAsync(string? email, HttpContext httpContext);

    /// <summary>Checks whether the account is currently locked (app-level).</summary>
    Task<(bool IsLocked, DateTime? LockedUntil)> IsAccountLockedAsync(string? email);

    /// <summary>Records a login attempt to the audit table.</summary>
    Task RecordLoginAttemptAsync(string email, string? userId, string ip, string userAgent,
        string result, string? failureReason, bool captchaRequired, bool captchaPassed);

    /// <summary>Called after a failed login – increments counter and may lock the account.</summary>
    Task<LoginFailureAction> HandleFailedLoginAsync(ApplicationUser user);

    /// <summary>Called after a successful login – resets counters and sets trusted cookie.</summary>
    Task HandleSuccessfulLoginAsync(ApplicationUser user, HttpContext httpContext);

    /// <summary>Checks whether a password reset request should be rate-limited.</summary>
    Task<bool> IsPasswordResetAllowedAsync(ApplicationUser user);

    /// <summary>Records that a password reset was requested (for rate limiting).</summary>
    Task RecordPasswordResetRequestAsync(ApplicationUser user);

    /// <summary>Checks for the trusted-device cookie.</summary>
    bool HasTrustedDeviceCookie(HttpContext httpContext);

    /// <summary>Sets the trusted-device cookie.</summary>
    void SetTrustedDeviceCookie(HttpContext httpContext);

    /// <summary>Unlocks a user's account (admin action).</summary>
    Task UnlockAccountAsync(ApplicationUser user);

    /// <summary>Returns the count of consecutive failures for a user (for UI display).</summary>
    Task<int> GetConsecutiveFailuresAsync(string? email);

    /// <summary>Retrieves recent login attempts for admin viewing.</summary>
    Task<(List<LoginAttempt> Items, int TotalCount)> GetLoginAttemptsAsync(
        string? emailFilter, string? resultFilter, DateTime? fromDate, DateTime? toDate,
        int page, int pageSize);

    /// <summary>Purges old audit records based on retention settings.</summary>
    Task PurgeOldAuditRecordsAsync();
}

// ==================== Implementation ====================

public sealed class LoginSecurityService : ILoginSecurityService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMemoryCache _cache;
    private readonly IEmailService _emailService;
    private readonly LoginSecuritySettings _settings;
    private readonly ILogger<LoginSecurityService> _logger;

    private const string TrustedDeviceCookieName = "LearnLink_TrustedDevice";
    private const string IpRateCachePrefix = "LoginRate_IP_";

    public LoginSecurityService(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IMemoryCache cache,
        IEmailService emailService,
        IOptions<LoginSecuritySettings> settings,
        ILogger<LoginSecurityService> logger)
    {
        _context = context;
        _userManager = userManager;
        _cache = cache;
        _emailService = emailService;
        _settings = settings.Value;
        _logger = logger;
    }

    // ===================== CAPTCHA Decision =====================

    public async Task<bool> ShouldRequireCaptchaAsync(string? email, HttpContext httpContext)
    {
        // 1. Check IP-level rate (high frequency from same IP)
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (IsIpRateSuspicious(ip))
            return true;

        // 2. No trusted device cookie → require CAPTCHA
        if (!HasTrustedDeviceCookie(httpContext))
            return true;

        // 3. If email is provided, check consecutive failure count
        if (!string.IsNullOrWhiteSpace(email))
        {
            var failures = await GetConsecutiveFailuresAsync(email);
            if (failures >= _settings.CaptchaThreshold)
                return true;
        }

        return false;
    }

    // ===================== Account Lock =====================

    public async Task<(bool IsLocked, DateTime? LockedUntil)> IsAccountLockedAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return (false, null);

        var user = await _userManager.FindByEmailAsync(email.Trim());
        if (user == null)
            return (false, null);

        if (user.AccountLockedUntil.HasValue && user.AccountLockedUntil.Value > DateTime.UtcNow)
            return (true, user.AccountLockedUntil.Value);

        // Auto-clear expired lock
        if (user.AccountLockedUntil.HasValue)
        {
            user.AccountLockedUntil = null;
            user.ConsecutiveFailedLogins = 0;
            await _userManager.UpdateAsync(user);
        }

        return (false, null);
    }

    // ===================== Audit Logging =====================

    public async Task RecordLoginAttemptAsync(string email, string? userId, string ip, string userAgent,
        string result, string? failureReason, bool captchaRequired, bool captchaPassed)
    {
        var attempt = new LoginAttempt
        {
            Email = email?.Trim() ?? "",
            UserId = userId,
            IpAddress = ip,
            UserAgent = userAgent.Length > 512 ? userAgent[..512] : userAgent,
            Result = result,
            FailureReason = failureReason,
            WasCaptchaRequired = captchaRequired,
            WasCaptchaPassed = captchaPassed,
            AttemptedAt = DateTime.UtcNow
        };

        _context.LoginAttempts.Add(attempt);
        await _context.SaveChangesAsync();

        // Track IP-level rate in cache
        IncrementIpRate(ip);
    }

    // ===================== Failed Login Handling =====================

    public async Task<LoginFailureAction> HandleFailedLoginAsync(ApplicationUser user)
    {
        user.ConsecutiveFailedLogins++;
        user.LastFailedLoginAt = DateTime.UtcNow;

        if (user.ConsecutiveFailedLogins >= _settings.LockoutThreshold)
        {
            user.AccountLockedUntil = DateTime.UtcNow.AddMinutes(_settings.LockoutDurationMinutes);
            await _userManager.UpdateAsync(user);

            _logger.LogWarning(
                "Account locked for {Email} after {Failures} consecutive failures. Locked until {LockedUntil}",
                user.Email, user.ConsecutiveFailedLogins, user.AccountLockedUntil);

            // Send lockout email
            if (_settings.SendLockoutEmail && _emailService.IsConfigured && !string.IsNullOrWhiteSpace(user.Email))
            {
                try
                {
                    var safeName = string.IsNullOrWhiteSpace(user.FirstName) ? "there" : user.FirstName;
                    var body = $@"
                        <div style=""font-family:Segoe UI,Arial,sans-serif;color:#1e293b;line-height:1.6"">
                            <h2 style=""margin-bottom:12px;color:#dc2626;"">Security Alert: Account Temporarily Locked</h2>
                            <p>Hello {System.Net.WebUtility.HtmlEncode(safeName)},</p>
                            <p>Your LearnLink account has been temporarily locked due to <strong>{user.ConsecutiveFailedLogins} consecutive failed login attempts</strong>.</p>
                            <p>Your account will be automatically unlocked after <strong>{_settings.LockoutDurationMinutes} minutes</strong>.</p>
                            <p>If this was not you, we recommend resetting your password immediately after the lockout period ends.</p>
                            <p style=""font-size:13px;color:#64748b;"">If you did not attempt to sign in, someone may be trying to access your account. Consider changing your password and contacting your administrator.</p>
                        </div>";
                    await _emailService.SendAsync(user.Email, "Security Alert: Account Temporarily Locked", body);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send lockout notification email to {Email}", user.Email);
                }
            }

            return LoginFailureAction.LockAccount;
        }

        await _userManager.UpdateAsync(user);

        if (user.ConsecutiveFailedLogins >= _settings.CaptchaThreshold)
        {
            _logger.LogInformation(
                "CAPTCHA warning triggered for {Email} after {Failures} consecutive failures",
                user.Email, user.ConsecutiveFailedLogins);
            return LoginFailureAction.ShowCaptchaWarning;
        }

        return LoginFailureAction.None;
    }

    // ===================== Successful Login Handling =====================

    public async Task HandleSuccessfulLoginAsync(ApplicationUser user, HttpContext httpContext)
    {
        if (user.ConsecutiveFailedLogins > 0 || user.AccountLockedUntil.HasValue)
        {
            user.ConsecutiveFailedLogins = 0;
            user.LastFailedLoginAt = null;
            user.AccountLockedUntil = null;
            await _userManager.UpdateAsync(user);
        }

        SetTrustedDeviceCookie(httpContext);
    }

    // ===================== Password Reset Rate Limiting =====================

    public async Task<bool> IsPasswordResetAllowedAsync(ApplicationUser user)
    {
        // If the window has expired (> 1 hour), allow and reset
        if (!user.PasswordResetWindowStart.HasValue ||
            (DateTime.UtcNow - user.PasswordResetWindowStart.Value).TotalHours >= 1)
        {
            return true;
        }

        return user.PasswordResetRequestCount < _settings.MaxPasswordResetsPerHour;
    }

    public async Task RecordPasswordResetRequestAsync(ApplicationUser user)
    {
        if (!user.PasswordResetWindowStart.HasValue ||
            (DateTime.UtcNow - user.PasswordResetWindowStart.Value).TotalHours >= 1)
        {
            user.PasswordResetWindowStart = DateTime.UtcNow;
            user.PasswordResetRequestCount = 1;
        }
        else
        {
            user.PasswordResetRequestCount++;
        }

        await _userManager.UpdateAsync(user);
    }

    // ===================== Trusted Device Cookie =====================

    public bool HasTrustedDeviceCookie(HttpContext httpContext)
    {
        return httpContext.Request.Cookies.ContainsKey(TrustedDeviceCookieName);
    }

    public void SetTrustedDeviceCookie(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Append(TrustedDeviceCookieName, Guid.NewGuid().ToString("N"), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(_settings.TrustedDeviceCookieDays),
            IsEssential = false
        });
    }

    // ===================== Admin Actions =====================

    public async Task UnlockAccountAsync(ApplicationUser user)
    {
        user.ConsecutiveFailedLogins = 0;
        user.LastFailedLoginAt = null;
        user.AccountLockedUntil = null;
        await _userManager.UpdateAsync(user);

        // Also clear Identity lockout
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        _logger.LogInformation("Account unlocked manually for {Email}", user.Email);
    }

    public async Task<int> GetConsecutiveFailuresAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return 0;

        var user = await _userManager.FindByEmailAsync(email.Trim());
        return user?.ConsecutiveFailedLogins ?? 0;
    }

    public async Task<(List<LoginAttempt> Items, int TotalCount)> GetLoginAttemptsAsync(
        string? emailFilter, string? resultFilter, DateTime? fromDate, DateTime? toDate,
        int page, int pageSize)
    {
        var query = _context.LoginAttempts.AsQueryable();

        if (!string.IsNullOrWhiteSpace(emailFilter))
            query = query.Where(la => la.Email.Contains(emailFilter.Trim()));

        if (!string.IsNullOrWhiteSpace(resultFilter))
            query = query.Where(la => la.Result == resultFilter);

        if (fromDate.HasValue)
            query = query.Where(la => la.AttemptedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(la => la.AttemptedAt <= toDate.Value.AddDays(1));

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(la => la.AttemptedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task PurgeOldAuditRecordsAsync()
    {
        if (_settings.AuditLogRetentionDays <= 0)
            return;

        var cutoff = DateTime.UtcNow.AddDays(-_settings.AuditLogRetentionDays);
        var oldRecords = await _context.LoginAttempts
            .Where(la => la.AttemptedAt < cutoff)
            .ToListAsync();

        if (oldRecords.Count > 0)
        {
            _context.LoginAttempts.RemoveRange(oldRecords);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Purged {Count} login audit records older than {Days} days", oldRecords.Count, _settings.AuditLogRetentionDays);
        }
    }

    // ===================== IP Rate Tracking (in-memory) =====================

    private bool IsIpRateSuspicious(string ip)
    {
        var key = $"{IpRateCachePrefix}{ip}";
        if (_cache.TryGetValue<int>(key, out var count))
        {
            // More than 10 login attempts from same IP in 5 minutes is suspicious
            return count > 10;
        }
        return false;
    }

    private void IncrementIpRate(string ip)
    {
        var key = $"{IpRateCachePrefix}{ip}";
        var count = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return 0;
        });
        _cache.Set(key, count + 1, TimeSpan.FromMinutes(5));
    }
}
