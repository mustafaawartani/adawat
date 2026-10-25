using Adawat.Translator;
using Xunit;

namespace Adawat.Tests;

public class TranslatorTests
{
    private const string SampleJson = """
        {"sentences":[{"trans":"الثعلب البني السريع","orig":"the quick brown fox","backend":10},{"translit":"alththaelab albuniyu alsarie","src_translit":""}],
         "dict":[{"pos":"noun","terms":["ثعلب","مكار"],"entry":[{"word":"ثعلب","reverse_translation":["fox"],"score":0.5}],"base_form":"fox","pos_enum":1}],
         "src":"en","confidence":1.0,"ld_result":{"srclangs":["en"],"srclangs_confidences":[1.0],"extended_srclangs":["en"]}}
        """;

    [Fact]
    public void ParsesTranslationDictionaryAndLanguage()
    {
        var result = GoogleResponseParser.Parse(SampleJson, "the quick brown fox", "ar");
        Assert.Equal("الثعلب البني السريع", result.Translation);
        Assert.Equal("en", result.SourceLanguage);
        Assert.Equal("ar", result.TargetLanguage);
        Assert.Equal("alththaelab albuniyu alsarie", result.Transliteration);
        var entry = Assert.Single(result.Dictionary);
        Assert.Equal("noun", entry.PartOfSpeech);
        Assert.Equal(new[] { "ثعلب", "مكار" }, entry.Terms);
    }

    [Fact]
    public void JoinsMultipleSentences()
    {
        var json = """{"sentences":[{"trans":"Hola. ","orig":"Hello. "},{"trans":"¿Cómo estás?","orig":"How are you?"}],"src":"en"}""";
        Assert.Equal("Hola. ¿Cómo estás?", GoogleResponseParser.Parse(json, "Hello. How are you?", "es").Translation);
    }

    [Fact]
    public void BadJsonGivesAFriendlyError()
    {
        Assert.Throws<TranslationException>(() => GoogleResponseParser.Parse("<html>", "x", "ar"));
    }

    [Fact]
    public void ShortTextIsOneChunk()
    {
        var chunk = Assert.Single(TextChunker.Split("Hello world.", 100));
        Assert.Equal("Hello world.", chunk.Text);
    }

    [Fact]
    public void LongTextSplitsAtSentencesAndKeepsAllText()
    {
        var sentence = "This is a sentence that is fairly long. ";
        var text = string.Concat(Enumerable.Repeat(sentence, 200)).Trim();
        var chunks = TextChunker.Split(text, 1500);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 1500));
        Assert.All(chunks.Take(chunks.Count - 1), c => Assert.EndsWith(".", c.Text));
        var rebuilt = string.Concat(chunks.Select(c => c.Text + c.Separator));
        Assert.Equal(text.Replace(" ", ""), rebuilt.Replace(" ", ""));
    }

    [Fact]
    public void ParagraphBreaksArePreserved()
    {
        var para = new string('a', 900) + ".";
        var text = para + "\n\n" + para + "\n\n" + para;
        var chunks = TextChunker.Split(text, 1500);
        Assert.Equal(3, chunks.Count);
        Assert.Equal("\n\n", chunks[0].Separator);
    }

    [Theory]
    [InlineData("ar", true)]
    [InlineData("iw", true)]
    [InlineData("fa", true)]
    [InlineData("en", false)]
    [InlineData("es", false)]
    public void KnowsRightToLeftLanguages(string code, bool rtl) => Assert.Equal(rtl, Languages.IsRightToLeft(code));

    [Theory]
    [InlineData("مرحبا بك", true)]
    [InlineData("  123 مرحبا", true)]
    [InlineData("Hello مرحبا", false)]
    [InlineData("", false)]
    public void DetectsRightToLeftText(string text, bool rtl) => Assert.Equal(rtl, Languages.LooksRightToLeft(text));

    [Fact]
    public void FavoritesComeFirst()
    {
        var ordered = Languages.Ordered(new[] { "es", "ar" });
        Assert.Equal("es", ordered[0].Code);
        Assert.Equal("ar", ordered[1].Code);
        Assert.Equal(Languages.All.Count, ordered.Count);
    }

    [Fact]
    public void NamesLanguages()
    {
        Assert.Equal("Arabic", Languages.NameOf("ar"));
        Assert.Equal("Spanish", Languages.NameOf("es"));
        Assert.Equal("Chinese (Simplified)", Languages.NameOf("zh-CN"));
        Assert.Equal("Hebrew", Languages.NameOf("he"));
        Assert.True(Languages.Same("he", "iw"));
    }
}
