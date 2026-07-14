using System.Text.RegularExpressions;

namespace HelloPubSub;

/// <summary>
/// Provides lightweight normalization for OCR text before it is stored in Firestore.
/// </summary>
public static class OcrTextCleaner
{
    /// <summary>
    /// Cleans raw OCR text and returns raw, display-ready, and searchable versions.
    /// </summary>
    /// <param name="rawText">Raw text extracted by Cloud Vision.</param>
    /// <returns>A model containing the original text, cleaned text, and lowercase search text.</returns>
    public static CleanMenuResultModel Clean(string rawText)
    {
        // Empty OCR input should still return a valid result object.
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new CleanMenuResultModel
            {
                RawText = "",
                CleanText = "",
                SearchText = ""
            };
        }

        string clean = rawText;

        // Normalize line endings from different operating systems
        clean = clean.Replace("\r\n", "\n");
        clean = clean.Replace("\r", "\n");

        // Normalize spacing while keeping line breaks between menu sections/items.
        clean = Regex.Replace(clean, @"[ \t]+", " ");
        clean = Regex.Replace(clean, @"\n{2,}", "\n");

        clean = clean.Trim();

        string search = clean.ToLowerInvariant();

        return new CleanMenuResultModel
        {
            RawText = rawText,
            CleanText = clean,
            SearchText = search
        };
    }
}