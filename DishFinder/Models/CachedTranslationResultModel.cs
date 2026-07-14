namespace DishFinder.Models
{
    public class CachedTranslationResultModel
    {
        public string OriginalText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public string TargetLanguage { get; set; } = string.Empty;
        public string SourceLanguage { get; set; } = "auto";
        public DateTime CachedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
