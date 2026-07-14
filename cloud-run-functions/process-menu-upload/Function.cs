using CloudNative.CloudEvents;
using Google.Cloud.Firestore;
using Google.Cloud.Functions.Framework;
using Google.Cloud.Vision.V1;
using Google.Events.Protobuf.Cloud.PubSub.V1;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HelloPubSub;

/// <summary>
/// Pub/Sub payload created by the web app when a menu image is uploaded.
/// Contains the Firestore document IDs and Cloud Storage object details needed for OCR processing.
/// </summary>
public class MenuUploadMessage
{
    public string RestaurantId { get; set; } = string.Empty;
    public string MenuId { get; set; } = string.Empty;
    public string ImageId { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string GsUri { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
}

/// <summary>
/// Cloud Run function triggered by Pub/Sub after a menu image upload.
/// It extracts OCR text with Cloud Vision and stores both page-level and menu-level OCR data in Firestore.
/// </summary>
public class Function : ICloudEventFunction<MessagePublishedData>
{
    private readonly ILogger _logger;
    private readonly ImageAnnotatorClient _visionClient;
    private readonly FirestoreDb _firestoreDb;

    /// <summary>
    /// Creates the Cloud Vision and Firestore clients using the current Google Cloud project.
    /// </summary>
    /// <param name="logger">Logger used for processing diagnostics.</param>
    /// <exception cref="InvalidOperationException">Thrown when the Google Cloud project ID is missing.</exception>
    public Function(ILogger<Function> logger)
    {
        _logger = logger;

        string projectId =
            Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
            ?? throw new InvalidOperationException("Missing GOOGLE_CLOUD_PROJECT environment variable.");

        _visionClient = ImageAnnotatorClient.Create();
        _firestoreDb = new FirestoreDbBuilder
        {
            ProjectId = projectId
        }.Build();
    }

    /// <summary>
    /// Handles one Pub/Sub image-upload event, extracts OCR text, and updates Firestore documents.
    /// </summary>
    /// <param name="cloudEvent">CloudEvent metadata supplied by the Functions Framework.</param>
    /// <param name="data">Pub/Sub message data containing the uploaded image details.</param>
    /// <param name="cancellationToken">Cancellation token passed to Firestore operations.</param>
    /// <returns>A task representing the asynchronous processing operation.</returns>
    public async Task HandleAsync(
        CloudEvent cloudEvent,
        MessagePublishedData data,
        CancellationToken cancellationToken)
    {
        // Read and validate the Pub/Sub payload.
        string? json = data.Message?.TextData;

        if (string.IsNullOrWhiteSpace(json))
        {
            _logger.LogWarning("Received empty Pub/Sub message body.");
            return;
        }

        _logger.LogInformation("Raw Pub/Sub payload: {Json}", json);

        MenuUploadMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<MenuUploadMessage>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Pub/Sub payload.");
            return;
        }

        if (message == null)
        {
            _logger.LogWarning("Pub/Sub payload deserialized to null.");
            return;
        }

        try
        {
            // Extract OCR text from the uploaded Cloud Storage image
            Image image = Image.FromUri(message.GsUri);
            TextAnnotation textAnnotation = _visionClient.DetectDocumentText(image);

           string rawOcrText = textAnnotation?.Text?.Trim() ?? string.Empty;

            // Clean page-level OCR before saving it against the image document
            CleanMenuResultModel currentPage = OcrTextCleaner.Clean(rawOcrText);

            _logger.LogInformation(
                "OCR extracted for MenuId={MenuId}, ImageId={ImageId}, RawLength={RawLength}, CleanLength={CleanLength}",
                message.MenuId,
                message.ImageId,
                currentPage.RawText.Length,
                currentPage.CleanText.Length);

            // Store OCR fields on the uploaded image document
            DocumentReference imageRef = _firestoreDb
                .Collection("restaurants")
                .Document(message.RestaurantId)
                .Collection("menus")
                .Document(message.MenuId)
                .Collection("images")
                .Document(message.ImageId);

            var imageUpdate = new Dictionary<string, object>
            {
                ["ocrTextRaw"] = currentPage.RawText,
                ["ocrText"] = currentPage.CleanText,
                ["ocrSearchText"] = currentPage.SearchText,
                ["processedAt"] = FieldValue.ServerTimestamp,
                ["updatedAt"] = FieldValue.ServerTimestamp
            };

            await imageRef.SetAsync(imageUpdate, SetOptions.MergeAll);

            // Read all image OCR pages so the menu document has one combined OCR body
            QuerySnapshot imageSnapshots = await _firestoreDb
                .Collection("restaurants")
                .Document(message.RestaurantId)
                .Collection("menus")
                .Document(message.MenuId)
                .Collection("images")
                .OrderBy("uploadedAt")
                .GetSnapshotAsync(cancellationToken);

            var allRawPages = new List<string>();

            foreach (DocumentSnapshot doc in imageSnapshots.Documents)
            {
                var dict = doc.ToDictionary();

                if (dict.TryGetValue("ocrTextRaw", out object? rawObj) &&
                    rawObj is string raw &&
                    !string.IsNullOrWhiteSpace(raw))
                {
                    allRawPages.Add(raw);
                }
            }

            // Combine OCR from all uploaded pages into the parent menu document
            string combinedRawText = string.Join("\n\n", allRawPages);
            CleanMenuResultModel combinedMenu = OcrTextCleaner.Clean(combinedRawText);

            DocumentReference menuRef = _firestoreDb
                .Collection("restaurants")
                .Document(message.RestaurantId)
                .Collection("menus")
                .Document(message.MenuId);

            var menuUpdate = new Dictionary<string, object>
            {
                ["ocrTextRaw"] = combinedMenu.RawText,
                ["ocrText"] = combinedMenu.CleanText,
                ["ocrSearchText"] = combinedMenu.SearchText,
                ["lastProcessedAt"] = FieldValue.ServerTimestamp,
                ["updatedAt"] = FieldValue.ServerTimestamp,
                ["status"] = "pending"
            };

            await menuRef.SetAsync(menuUpdate, SetOptions.MergeAll);

            // Mark the restaurant as pending until the parser converts OCR into menu items
            DocumentReference restaurantRef = _firestoreDb.Collection("restaurants").Document(message.RestaurantId);

            var restaurantUpdate = new Dictionary<string, object>
            {
                ["status"] = "pending",
                ["updatedAt"] = FieldValue.ServerTimestamp
            };

            await restaurantRef.SetAsync(restaurantUpdate, SetOptions.MergeAll);

            _logger.LogInformation(
                "Firestore updated. RestaurantId={RestaurantId}, MenuId={MenuId}",
                message.RestaurantId,
                message.MenuId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process OCR for RestaurantId={RestaurantId}, MenuId={MenuId}, ImageId={ImageId}",
                message.RestaurantId,
                message.MenuId,
                message.ImageId);

            throw;
        }
    }
}