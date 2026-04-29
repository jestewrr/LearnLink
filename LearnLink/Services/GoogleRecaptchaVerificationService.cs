using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LearnLink.Services;

public class RecaptchaSettings
{
    public string SiteKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
}

/// <summary>
/// Google reCAPTCHA v2 ("I'm not a robot" checkbox) verification service.
/// Calls Google's siteverify API to validate the user's captcha response token.
/// </summary>
public sealed class GoogleRecaptchaVerificationService : ICaptchaVerificationService
{
    private readonly RecaptchaSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleRecaptchaVerificationService> _logger;

    private const string VerifyUrl = "https://www.google.com/recaptcha/api/siteverify";

    public GoogleRecaptchaVerificationService(
        IOptions<RecaptchaSettings> options,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleRecaptchaVerificationService> logger)
    {
        _settings = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(_settings.SiteKey) &&
        !string.IsNullOrWhiteSpace(_settings.SecretKey);

    public string SiteKey => _settings.SiteKey;

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default)
    {
        // If reCAPTCHA is not configured, allow the request (graceful degradation)
        if (!IsEnabled)
            return true;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var client = _httpClientFactory.CreateClient();

            var formData = new Dictionary<string, string>
            {
                { "secret", _settings.SecretKey },
                { "response", token }
            };

            // Include the user's IP if available for additional verification
            if (!string.IsNullOrWhiteSpace(remoteIp))
                formData["remoteip"] = remoteIp;

            var response = await client.PostAsync(
                VerifyUrl,
                new FormUrlEncodedContent(formData),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("reCAPTCHA siteverify returned status {StatusCode}", response.StatusCode);
                return false;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
                return true;

            // Log error codes for debugging
            if (root.TryGetProperty("error-codes", out var errorCodes))
            {
                _logger.LogWarning("reCAPTCHA verification failed with errors: {Errors}", errorCodes.GetRawText());
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "reCAPTCHA verification threw an exception");
            // On network failures, deny the request to be safe
            return false;
        }
    }
}
