using DishFinder.Interfaces;
using DishFinder.Models;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DishFinder.Services
{
    public class MenuTranslationService : IMenuTranslationService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ITranslationCacheService _translationCacheService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MenuTranslationService> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public MenuTranslationService(
            IHttpClientFactory httpClientFactory,
            ITranslationCacheService translationCacheService,
            IConfiguration configuration,
            ILogger<MenuTranslationService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _translationCacheService = translationCacheService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<CachedTranslationResultModel> TranslateAsync(
            string restaurantId,
            string menuId,
            string itemName,
            string targetLanguage,
            string? sourceLanguage = "auto")
        {
            // Check Redis cache first 
            CachedTranslationResultModel? cached =
                await _translationCacheService.GetAsync(
                    restaurantId,
                    menuId,
                    itemName,
                    targetLanguage);

            if (cached is not null)
            {
                _logger.LogInformation(
                    "Translation cache hit. RestaurantId={RestaurantId}, MenuId={MenuId}, ItemName={ItemName}, TargetLanguage={TargetLanguage}",
                    restaurantId, menuId, itemName, targetLanguage);

                return cached;
            }

            _logger.LogInformation(
                "Translation cache miss. RestaurantId={RestaurantId}, MenuId={MenuId}, ItemName={ItemName}, TargetLanguage={TargetLanguage}",
                restaurantId, menuId, itemName, targetLanguage);

            // Call translate-menu-item Cloud Run function
            string serviceUrl =
                _configuration["CloudFunctions:TranslateMenuItemUrl"]
                ?? throw new InvalidOperationException("Missing CloudFunctions:TranslateMenuItemUrl");

            var client = _httpClientFactory.CreateClient("TranslateMenuItem");

            bool requireAuth =
                !string.Equals(
                    _configuration["CloudFunctions:TranslateMenuItemRequireAuth"],
                    "false",
                    StringComparison.OrdinalIgnoreCase);

            if (requireAuth)
            {
                string idToken = await GetIdTokenAsync(serviceUrl);

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", idToken);
            }

            object requestBody = string.IsNullOrWhiteSpace(sourceLanguage)
                ? new
                {
                    text = itemName,
                    targetLanguage = targetLanguage
                }
                : new
                {
                    text = itemName,
                    targetLanguage = targetLanguage,
                    sourceLanguage = sourceLanguage
                };

            using var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response = await client.PostAsync("", content);

            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "translate-menu-item failed. StatusCode={StatusCode}, Body={Body}",
                    (int)response.StatusCode,
                    responseJson);

                throw new InvalidOperationException(
                    $"translate-menu-item failed with status {(int)response.StatusCode}: {responseJson}");
            }

            TranslateFunctionResponse? translated =
                JsonSerializer.Deserialize<TranslateFunctionResponse>(responseJson, JsonOptions);

            if (translated is null || !translated.Success || string.IsNullOrWhiteSpace(translated.TranslatedText))
            {
                throw new InvalidOperationException("Translation response was invalid.");
            }

            var result = new CachedTranslationResultModel
            {
                OriginalText = translated.OriginalText ?? itemName,
                TranslatedText = translated.TranslatedText,
                TargetLanguage = translated.TargetLanguage ?? targetLanguage,
                SourceLanguage = translated.SourceLanguage ?? sourceLanguage ?? "auto",
                CachedAtUtc = DateTime.UtcNow
            };

            // Store in Redis
            await _translationCacheService.SetAsync(
                restaurantId,
                menuId,
                itemName,
                targetLanguage,
                result);

            return result;
        }

        private static async Task<string> GetIdTokenAsync(string audience)
        {
            GoogleCredential credential = await GoogleCredential.GetApplicationDefaultAsync();
            OidcToken oidcToken =
                await credential.GetOidcTokenAsync(OidcTokenOptions.FromTargetAudience(audience));

            return await oidcToken.GetAccessTokenAsync();
        }

        private sealed class TranslateFunctionResponse
        {
            public bool Success { get; set; }
            public string? OriginalText { get; set; }
            public string? TranslatedText { get; set; }
            public string? TargetLanguage { get; set; }
            public string? SourceLanguage { get; set; }
        }
    }
}
