using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class TranslateServiceTests
{
    [Fact]
    public void ParseResponseReadsStructuredTranslationAndDetection()
    {
        var result = TranslateService.ParseResponse("""
            {
              "translatedText": "Hola, mundo.",
              "detectedSourceLanguage": "English",
              "detectedSourceLanguageCode": "en",
              "ambiguities": ["World may mean the planet or a domain."]
            }
            """);

        Assert.Equal("Hola, mundo.", result.TranslatedText);
        Assert.Equal("English", result.DetectedSourceLanguage);
        Assert.Equal("en", result.DetectedSourceLanguageCode);
        Assert.Single(result.Ambiguities);
    }

    [Fact]
    public void ParseResponseExtractsJsonFromModelNoiseAndCapsAmbiguities()
    {
        var ambiguityJson = string.Join(",", Enumerable.Range(1, 12).Select(index => $"\"item {index}\""));
        var response = $"Model preface {{\"translatedText\":\"Bonjour\",\"detectedSourceLanguage\":\"English\",\"detectedSourceLanguageCode\":\"en\",\"ambiguities\":[{ambiguityJson}]}} trailing text";

        var result = TranslateService.ParseResponse(response);

        Assert.Equal("Bonjour", result.TranslatedText);
        Assert.Equal(8, result.Ambiguities.Count);
        Assert.Equal("item 1", result.Ambiguities[0]);
        Assert.Equal("item 8", result.Ambiguities[^1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"translatedText\":\"\",\"detectedSourceLanguage\":\"English\",\"ambiguities\":[]}")]
    [InlineData("{broken}")]
    public void ParseResponseRejectsMissingOrMalformedTranslation(string response)
    {
        Assert.Throws<InvalidOperationException>(() => TranslateService.ParseResponse(response));
    }
}
