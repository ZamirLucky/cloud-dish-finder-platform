using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelloHttp;

/// <summary>
/// Parses cleaned OCR text into structured menu items that can be stored and searched in Firestore.
/// </summary>
public static class MenuParser
{
    /// <summary>
    /// Section headings commonly found in restaurant menus and used to categorise parsed items
    /// </summary>
    private static readonly HashSet<string> KnownSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "STARTERS",
        "SOUPS",
        "SALADS",
        "PASTA",
        "PIZZA",
        "MAINS",
        "MAIN COURSES",
        "DESSERTS",
        "DRINKS",
        "SIDES",
        "SHARING PLATES",
        "LIGHTER MAINS",
        "MEAT & POULTRY",
        "FISH & SEAFOOD",
        "VEGETARIAN",
        "PASTA & RISOTTO",
        "ANTIPASTI",
        "HELWA",
        "SOPEP"
    };

    // Price, section, and item recognisers used by the OCR parsing flow.
    private static readonly Regex PriceRegex = new(
        @"(?:€\s*)?(\d{1,3}(?:[.,]\d{2}))(?=\D|$)",
        RegexOptions.Compiled);

    private static readonly Regex SectionRegex = new(
        @"\b(STARTERS|SOUPS|SALADS|PASTA|PIZZA|MAINS|MAIN COURSES|DESSERTS|DRINKS|SIDES|SHARING PLATES|LIGHTER MAINS|MEAT & POULTRY|FISH & SEAFOOD|VEGETARIAN|PASTA & RISOTTO|ANTIPASTI|HELWA|SOPEP)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ItemRegex = new(
        @"(?<name>[A-ZÀ-Ý][A-Za-zÀ-ÿ'&/\-\s]{2,120}?)\s*(?<price>(?:€\s*)?\d{1,3}(?:[.,]\d{2}))\s*(?<desc>.*?)(?=(?:[A-ZÀ-Ý][A-Za-zÀ-ÿ'&/\-\s]{2,120}?\s*(?:€\s*)?\d{1,3}(?:[.,]\d{2}))|\b(?:STARTERS|SOUPS|SALADS|PASTA|PIZZA|MAINS|MAIN COURSES|DESSERTS|DRINKS|SIDES|SHARING PLATES|LIGHTER MAINS|MEAT & POULTRY|FISH & SEAFOOD|VEGETARIAN|PASTA & RISOTTO|ANTIPASTI|HELWA|SOPEP)\b|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex MultiWhitespaceRegex = new(
        @"\s+",
        RegexOptions.Compiled);

    /// <summary>
    /// Counts menu-like prices in text before parsing, mainly for logging or parser diagnostics.
    /// </summary>
    /// <param name="text">OCR text to inspect.</param>
    /// <returns>The number of price patterns found.</returns>
    public static int CountPriceMatches(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return PriceRegex.Matches(text).Count;
    }

    /// <summary>
    /// Extracts menu items, prices, descriptions, and sections from OCR text.
    /// </summary>
    /// <param name="ocrText">Cleaned OCR text from the pending menu document.</param>
    /// <returns>A de-duplicated list of parsed menu items.</returns>
    public static List<ParsedMenuItemModel> Parse(string? ocrText)
    {
        var items = new List<ParsedMenuItemModel>();

        if (string.IsNullOrWhiteSpace(ocrText))
            return items;

        // Text preparation and regex matching.
        string text = PrepareText(ocrText);

        MatchCollection sectionMatches = SectionRegex.Matches(text);
        MatchCollection itemMatches = ItemRegex.Matches(text);

        foreach (Match match in itemMatches)
        {
            string rawName = match.Groups["name"].Value;
            string rawPrice = match.Groups["price"].Value;
            string rawDescription = match.Groups["desc"].Value;

            string? section = FindCurrentSection(sectionMatches, match.Index);
            string name = ExtractName(rawName, section);
            string description = CleanChunk(rawDescription);

            if (string.IsNullOrWhiteSpace(name))
                continue;

            double? priceValue = TryParsePrice(rawPrice);

            // Structured output for Firestore catalog/search.
            items.Add(new ParsedMenuItemModel
            {
                Name = name,
                Description = string.IsNullOrWhiteSpace(description) ? "" : description,
                PriceText = "€" + rawPrice.Replace("€", "").Trim().Replace(",", "."),
                PriceValue = priceValue,
                Section = section ?? "UNCATEGORISED",
                NormalizedName = NormalizeName(name)
            });
        }

        // Remove duplicated OCR matches for the same item and price
        return items
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => $"{x.NormalizedName}|{x.PriceText}")
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Normalises OCR text by removing common menu noise and improving section boundaries.
    /// </summary>
    /// <param name="text">Raw or cleaned OCR text.</param>
    /// <returns>Parser-ready text.</returns>
    private static string PrepareText(string text)
    {
        text = text.Replace("\r\n", "\n")
                   .Replace('\r', '\n')
                   .Replace('•', ' ');

        // Remove common footer/contact noise from OCR text.
        text = Regex.Replace(text, @"Page\s+\d+\s+of\s+\d+", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\+\d[\d\s]{5,}", " ");
        text = Regex.Replace(text, @"\b[a-z0-9.-]+\.(com|mt|net|org)\b", " ", RegexOptions.IgnoreCase);

        // Make section headers easier to detect and Normalize spacing produced by OCR
        text = SectionRegex.Replace(text, "\n$1\n");
        text = MultiWhitespaceRegex.Replace(text, " ").Trim();

        // Ignore restaurant/header text before the first section.
        Match firstSectionMatch = SectionRegex.Match(text);
        if (firstSectionMatch.Success && firstSectionMatch.Index > 0)
        {
            text = text[firstSectionMatch.Index..];
        }

        return text;
    }

    /// <summary>
    /// Finds the nearest section heading that appears before the current item match.
    /// </summary>
    /// <param name="sectionMatches">All section matches found in the menu text.</param>
    /// <param name="itemIndex">Position of the current item match.</param>
    /// <returns>The current section name, or null when no section has been found.</returns>
    private static string? FindCurrentSection(MatchCollection sectionMatches, int itemIndex)
    {
        string? currentSection = null;

        foreach (Match sectionMatch in sectionMatches)
        {
            if (sectionMatch.Index < itemIndex)
                currentSection = sectionMatch.Value.Trim();
            else
                break;
        }

        return currentSection;
    }

    /// <summary>
    /// Cleans an item name and removes section text that OCR may have merged into the item title.
    /// </summary>
    /// <param name="rawName">Raw name captured by the item regex.</param>
    /// <param name="currentSection">Section currently assigned to the item.</param>
    /// <returns>A cleaned item name.</returns>
    private static string ExtractName(string rawName, string? currentSection)
    {
        string name = CleanChunk(rawName);

        if (!string.IsNullOrWhiteSpace(currentSection))
        {
            name = Regex.Replace(
                name,
                $@"\b{Regex.Escape(currentSection)}\b",
                "",
                RegexOptions.IgnoreCase);
        }

        name = SectionRegex.Replace(name, " ");
        name = CleanChunk(name);

        // Keep the likely item title if OCR joined it with a longer heading.
        string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 6)
        {
            name = string.Join(" ", parts.Skip(parts.Length - 4));
        }

        return name.Trim();
    }

     /// <summary>
    /// Normalises punctuation and repeated whitespace in a captured OCR fragment.
    /// </summary>
    /// <param name="text">OCR fragment to clean.</param>
    /// <returns>A cleaned text fragment with normalized whitespace and punctuation.</returns>
    private static string CleanChunk(string text)
    {
        text = MultiWhitespaceRegex.Replace(text, " ").Trim();

        text = text.Replace(" ,", ",")
                   .Replace(" .", ".")
                   .Replace(" :", ":")
                   .Replace(" ;", ";");

        return text;
    }

    /// <summary>
    /// Converts a price string such as €5.50 or 5,50 into a numeric value for sorting.
    /// </summary>
    /// <param name="input">Price text captured from OCR.</param>
    /// <returns>The parsed price value, or null if parsing fails.</returns>
    private static double? TryParsePrice(string input)
    {
        string normalized = input.Replace("€", "").Trim().Replace(",", ".");

        if (double.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out double value))
        {
            return value;
        }

        return null;
    } 

    /// <summary>
    /// Produces a lower-case key used for duplicate detection and search matching.
    /// </summary>
    /// <param name="name">Parsed item name.</param>
    /// <returns>A normalised item name.</returns>
    private static string NormalizeName(string name) =>
        MultiWhitespaceRegex.Replace(name.ToLowerInvariant().Trim(), " ");
}