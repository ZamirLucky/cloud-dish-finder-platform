using Google.Cloud.Functions.Framework;
using Google.Cloud.Translate.V3;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace HelloHttp;

/// <summary>
/// Request body accepted by the translate-menu-item Cloud Run function.
/// The MVC catalog sends menu text plus the target language selected by the user.
/// </summary>
public sealed class TranslateRequest
{
    public string? Text { get; set; }
    public string? TargetLanguage { get; set; }
    public string? SourceLanguage { get; set; }
}

/// <summary>
/// HTTP function that translates one menu text value using Google Cloud Translation API.
/// It is called by the DishFinder web app when a translation is not already available in Redis cache.
/// </summary>
public class Function : IHttpFunction
{
    private readonly ILogger<Function> _logger;
    private readonly TranslationServiceClient _translationClient;
    private readonly string _projectId;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Function(ILogger<Function> logger)
    {
        _logger = logger;

        _projectId =
            Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
            ?? throw new InvalidOperationException("Missing GOOGLE_CLOUD_PROJECT environment variable.");

        _translationClient = TranslationServiceClient.Create();
    }

    /// <summary>
    /// Handles the HTTP POST request, validates the input, translates the text,
    /// and returns a JSON response to the calling web application.
    /// </summary>
    public async Task HandleAsync(HttpContext context)
    {
        // Request validation
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            await context.Response.WriteAsync("Use POST.");
            return;
        }

        try
        {
            // Read request body
            TranslateRequest? request =
                await JsonSerializer.DeserializeAsync<TranslateRequest>(
                    context.Request.Body,
                    JsonOptions);

            if (request is null ||
                string.IsNullOrWhiteSpace(request.Text) ||
                string.IsNullOrWhiteSpace(request.TargetLanguage))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";

                await JsonSerializer.SerializeAsync(context.Response.Body, new
                {
                    success = false,
                    error = "Request must contain text and targetLanguage."
                });

                return;
            }

            // Translation API request
            var translateRequest = new TranslateTextRequest
            {
                Parent = $"projects/{_projectId}/locations/global",
                MimeType = "text/plain",
                TargetLanguageCode = request.TargetLanguage.Trim()
            };

            if (!string.IsNullOrWhiteSpace(request.SourceLanguage))
            {
                translateRequest.SourceLanguageCode = request.SourceLanguage.Trim();
            }

            translateRequest.Contents.Add(request.Text.Trim());

            TranslateTextResponse response =
                await _translationClient.TranslateTextAsync(translateRequest);

            var firstTranslation = response.Translations.FirstOrDefault();
            string translatedText = firstTranslation?.TranslatedText ?? string.Empty;

            // Operational logging
            _logger.LogInformation(
                "Translation completed. TargetLanguage={TargetLanguage}, SourceLength={SourceLength}, ResultLength={ResultLength}",
                request.TargetLanguage,
                request.Text.Length,
                translatedText.Length);

            // Success response
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";

            await JsonSerializer.SerializeAsync(context.Response.Body, new
            {
                success = true,
                originalText = request.Text,
                translatedText,
                targetLanguage = request.TargetLanguage,
                sourceLanguage = request.SourceLanguage ?? "auto"
            });
        }
        catch (Exception ex)
        {
            // Error response
            _logger.LogError(ex, "translate-menu-item failed");

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            await JsonSerializer.SerializeAsync(context.Response.Body, new
            {
                success = false,
                error = ex.Message
            });
        }
    }
}