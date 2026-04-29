using System.Threading;
using System.Threading.Tasks;

namespace LearnLink.Services;

public interface ICaptchaVerificationService
{
    bool IsEnabled { get; }
    string SiteKey { get; }
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default);
}

public sealed class NoopCaptchaVerificationService : ICaptchaVerificationService
{
    public bool IsEnabled => false;

    public string SiteKey => string.Empty;

    public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
