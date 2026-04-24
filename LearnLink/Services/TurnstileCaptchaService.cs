using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace LearnLink.Services;

public class TurnstileOptions
{
    public bool Enabled { get; set; }
    public string SiteKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string VerifyEndpoint { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
}

public interface ICaptchaVerificationService
{
    bool IsEnabled { get; }
    string SiteKey { get; }
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default);
}

public sealed class TurnstileCaptchaVerificationService : ICaptchaVerificationService
{
    private readonly HttpClient _httpClient;
    private readonly TurnstileOptions _options;
    private readonly ILogger<TurnstileCaptchaVerificationService> _logger;

    public TurnstileCaptchaVerificationService(
        HttpClient httpClient,
        IOptions<TurnstileOptions> options,
        ILogger<TurnstileCaptchaVerificationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.SiteKey) &&
        !string.IsNullOrWhiteSpace(_options.SecretKey);

    public string SiteKey => _options.SiteKey;

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return true;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        var formData = new Dictionary<string, string>
        {
            ["secret"] = _options.SecretKey,
            ["response"] = token
        };

        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            formData["remoteip"] = remoteIp;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.VerifyEndpoint)
            {
                Content = new FormUrlEncodedContent(formData)
            };

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Turnstile verification endpoint returned {StatusCode}", (int)response.StatusCode);
                return false;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var verification = JsonSerializer.Deserialize<TurnstileVerificationResponse>(payload);

            if (verification == null)
                return false;

            if (!verification.Success && verification.ErrorCodes?.Count > 0)
            {
                _logger.LogWarning("Turnstile verification failed: {ErrorCodes}", string.Join(",", verification.ErrorCodes));
            }

            return verification.Success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Turnstile verification failed due to exception.");
            return false;
        }
    }

    private sealed class TurnstileVerificationResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("error-codes")]
        public List<string>? ErrorCodes { get; set; }
    }
}
