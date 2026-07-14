using System.Collections.Generic;
/// <summary>
/// Holds the raw, cleaned, and searchable versions of OCR text produced during menu processing
/// </summary>
public sealed class CleanMenuResultModel
{
    // Original OCR text returned by Cloud Vision
    public string RawText { get; init; } = string.Empty;
    
    // Normalized OCR text with basic whitespace cleanup applied
    public string CleanText { get; init; } = string.Empty;

    // Lowercase text used for simple search and matching operations
    public string SearchText { get; init; } = string.Empty;
}