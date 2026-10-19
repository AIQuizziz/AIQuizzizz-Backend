using CleanArchitectureBase.Application.Questions.Utils;

namespace CleanArchitectureBase.Infrastructure.Flashcards;

internal static class FlashcardLimits
{
    public const int MaxMeanings = FlashcardNormalizer.MaxMeanings;
    public const int MaxText = FlashcardNormalizer.MaxTextLength;
}

internal static class FlashcardUrl
{
    public static bool IsHttps(string? url) => !string.IsNullOrWhiteSpace(url) && FlashcardNormalizer.IsHttpsUrl(url);
}
