using Google.Cloud.Firestore;
using Google.Cloud.Functions.Framework;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;

namespace HelloHttp
{
    /// <summary>
    /// Optional HTTP request body used to control how many pending menus are parsed in one run.
    /// </summary>
    public sealed class ParsePendingMenusRequest
    {
        // Maximum number of pending menu documents to process.
        public int? Limit { get; set; }
    }

    /// <summary>
    /// HTTP Cloud Function that finds pending menu OCR records, parses them into structured menu items,
    /// and updates Firestore so the catalog can search, sort, and display confirmed menu entries.
    /// </summary>
    public class Function : IHttpFunction
    {
        private readonly ILogger<Function> _logger;
        private readonly FirestoreDb _firestoreDb;

        // JSON options used when reading the optional request body
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Creates the parser function and connects it to the Firestore database for the active Google Cloud project.
        /// </summary>
        public Function(ILogger<Function> logger)
        {
            _logger = logger;

            string projectId =
                Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
                ?? throw new InvalidOperationException("Missing GOOGLE_CLOUD_PROJECT environment variable.");

            _firestoreDb = new FirestoreDbBuilder
            {
                ProjectId = projectId
            }.Build();
        }

        /// <summary>
        /// Handles a scheduled/manual POST request, parses pending menus, and returns a JSON processing summary.
        /// </summary>
        /// <param name="context">The HTTP request and response context supplied by the Functions Framework.</param>
        /// <returns>A task representing the asynchronous Firestore parsing workflow.</returns>
        public async Task HandleAsync(HttpContext context)
        {
            // Request validation.
            if (!HttpMethods.IsPost(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                await context.Response.WriteAsync("Use POST.");
                return;
            }

            int limit = 1;

            // Batch limit from query string.
            if (context.Request.Query.TryGetValue("limit", out var limitFromQuery) &&
                int.TryParse(limitFromQuery, out int parsedQueryLimit))
            {
                limit = parsedQueryLimit;
            }

            // Batch limit from optional JSON body.
            if (context.Request.ContentLength > 0 &&
                context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                ParsePendingMenusRequest? request =
                    await JsonSerializer.DeserializeAsync<ParsePendingMenusRequest>(
                        context.Request.Body,
                        JsonOptions);

                if (request?.Limit is int bodyLimit)
                {
                    limit = bodyLimit;
                }
            }

            // Enforce safe boundaries for processing batch size.
            limit = Math.Clamp(limit, 1, 20);

            try{
                // Firestore query: find pending menus across all restaurants.
                QuerySnapshot snapshot = await _firestoreDb
                    .CollectionGroup("menus")
                    .WhereEqualTo("status", "pending")
                    .Limit(limit)
                    .GetSnapshotAsync();

                var processed = new List<object>();

                foreach (DocumentSnapshot menuDoc in snapshot.Documents)
                {
                    string menuId = menuDoc.Id;
                    string restaurantId = menuDoc.Reference.Parent.Parent?.Id ?? "unknown";

                    string? menuTitle = menuDoc.ContainsField("menuTitle")
                        ? menuDoc.GetValue<string>("menuTitle")
                        : null;

                    string? ocrTextRaw = menuDoc.ContainsField("ocrTextRaw")
                        ? menuDoc.GetValue<string>("ocrTextRaw")
                        : null;

                    string? ocrText = menuDoc.ContainsField("ocrText")
                        ? menuDoc.GetValue<string>("ocrText")
                        : null;

                    // Parser input: use raw OCR where available, otherwise use cleaned OCR text.
                    string? parseSourceText = !string.IsNullOrWhiteSpace(ocrTextRaw) ? ocrTextRaw : ocrText;

                    if (string.IsNullOrWhiteSpace(parseSourceText))
                    {
                        processed.Add(new
                        {
                            MenuId = menuId,
                            RestaurantId = restaurantId,
                            MenuTitle = menuTitle,
                            ParsedItemCount = 0,
                            Result = "Skipped: empty ocrText"
                        });

                        _logger.LogWarning(
                            "Skipping menu because parse source text is empty. RestaurantId={RestaurantId}, MenuId={MenuId}",
                            restaurantId,
                            menuId);

                        continue;
                    }

                    // Diagnostic preview for OCR/parser quality checks.
                    _logger.LogInformation(
                        "Parse preview. MenuId={MenuId}, Preview={Preview}",
                        menuId,
                        parseSourceText.Length > 300 ? parseSourceText[..300] : parseSourceText);

                    _logger.LogInformation(
                        "Parser input stats. RestaurantId={RestaurantId}, MenuId={MenuId}, ParseSourceLength={Length}, UsingRaw={UsingRaw}, PriceMatchCount={PriceMatchCount}",
                        restaurantId,
                        menuId,
                        parseSourceText?.Length ?? 0,
                        !string.IsNullOrWhiteSpace(ocrTextRaw),
                        MenuParser.CountPriceMatches(parseSourceText));

                    // Menu parsing: extract item names, descriptions, sections, and price values.
                    List<ParsedMenuItemModel> parsedItems = MenuParser.Parse(parseSourceText);

                    _logger.LogInformation(
                        "Parsing finished. RestaurantId={RestaurantId}, MenuId={MenuId}, ParsedItemCount={ParsedItemCount}",
                        restaurantId,
                        menuId,
                        parsedItems.Count);

                    DocumentReference menuRef = _firestoreDb
                        .Collection("restaurants")
                        .Document(restaurantId)
                        .Collection("menus")
                        .Document(menuId);

                    string newStatus = parsedItems.Count > 0 ? "confirmed" : "pending";

                    // Menu update: store structured items for catalog search, price sorting, and translation.
                    var menuUpdate = new Dictionary<string, object>
                    {
                        ["parsedItems"] = parsedItems.Select(item => new Dictionary<string, object?>
                        {
                            ["name"] = item.Name,
                            ["description"] = item.Description,
                            ["priceText"] = item.PriceText,
                            ["priceValue"] = item.PriceValue,
                            ["section"] = item.Section,
                            ["normalizedName"] = item.NormalizedName
                        }).ToList(),
                        ["parsedAt"] = FieldValue.ServerTimestamp,
                        ["updatedAt"] = FieldValue.ServerTimestamp,
                        ["status"] = newStatus
                    };

                    await menuRef.SetAsync(menuUpdate, SetOptions.MergeAll);

                    // Restaurant status: confirm the restaurant when no pending menus remain.
                    QuerySnapshot remainingPendingMenus = await _firestoreDb
                        .Collection("restaurants")
                        .Document(restaurantId)
                        .Collection("menus")
                        .WhereEqualTo("status", "pending")
                        .Limit(1)
                        .GetSnapshotAsync();

                    if (remainingPendingMenus.Count == 0)
                    {
                        DocumentReference restaurantRef = _firestoreDb
                            .Collection("restaurants")
                            .Document(restaurantId);

                        await restaurantRef.SetAsync(new Dictionary<string, object>
                        {
                            ["status"] = "confirmed",
                            ["updatedAt"] = FieldValue.ServerTimestamp
                        }, SetOptions.MergeAll);
                    }

                    processed.Add(new
                    {
                        MenuId = menuId,
                        RestaurantId = restaurantId,
                        MenuTitle = menuTitle,
                        ParsedItemCount = parsedItems.Count,
                        Result = parsedItems.Count > 0 ? "Confirmed" : "No items parsed - kept pending"
                    });
                }

                _logger.LogInformation(
                    "parse-pending-menus completed. RequestedLimit={RequestedLimit}, Processed={ProcessedCount}",
                    limit,
                    processed.Count);

                // Success response for Cloud Scheduler/manual test calls.
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/json";

                await JsonSerializer.SerializeAsync(context.Response.Body, new
                {
                    success = true,
                    requestedLimit = limit,
                    processedCount = processed.Count,
                    processed
                });
                
            }catch (Exception ex)
            {
                // Error response: keep the scheduler call observable in logs and HTTP output.
                _logger.LogError(ex, "parse-pending-menus failed");

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
}