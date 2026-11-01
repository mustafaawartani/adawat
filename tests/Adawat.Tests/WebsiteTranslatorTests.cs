using Xunit;
using System.Text.Json;
using Adawat.Translator;

namespace Adawat.Tests;

public class WebsiteTranslatorTests
{
    /// <summary>An answer shaped like the one translate.google.com's service sends (rt=c: length-prefixed lines).</summary>
    private static string Answer(string inner) =>
        ")]}'\n\n" + "1234\n" + JsonSerializer.Serialize(new object?[] { new object?[] { "wrb.fr", "MkEWBc", inner, null, null, null, "generic" }, new object?[] { "di", 32 } }) +
        "\n57\n[[\"e\",4,null,null,1095]]\n";

    [Fact]
    public void ReadsTheTranslationDetectedLanguageAndPronunciation()
    {
        const string inner = "[[null,null,\"en\",[[[0,[[[null,11]],[true]]]],11],[[\"hello world\",null,null,11]]],"
                             + "[[[null,\"mrhba bialealam\",null,null,null,[[\"مرحبا بالعالم\",null,null,null,[[\"مرحبا بالعالم\",[2]]]]],null,null,null,[]]],\"ar\",1,\"en\",[\"hello world\",\"auto\",\"ar\",true]],\"en\"]";
        var result = WebsiteResponseParser.Parse(Answer(inner), "hello world", "auto", "ar");
        Assert.Equal("مرحبا بالعالم", result.Translation);
        Assert.Equal("en", result.SourceLanguage);
        Assert.Equal("mrhba bialealam", result.Transliteration);
    }

    [Fact]
    public void JoinsSentencesWithSpacesWhenTheAnswerSaysSo()
    {
        const string inner = "[[null,null,\"ar\"],[[[null,null,null,true,null,[[\"Hello.\"],[\"How are you?\"]]]],\"en\"],\"ar\"]";
        var result = WebsiteResponseParser.Parse(Answer(inner), "مرحبا. كيف حالك؟", "auto", "en");
        Assert.Equal("Hello. How are you?", result.Translation);
        Assert.Equal("ar", result.SourceLanguage);
    }

    [Fact]
    public void AnUnexpectedAnswerThrowsSoTheClassicServiceIsUsed()
    {
        Assert.Throws<TranslationException>(() => WebsiteResponseParser.Parse("<html>sorry</html>", "hi", "auto", "ar"));
        Assert.Throws<TranslationException>(() => WebsiteResponseParser.Parse(Answer("[[null],[[]]]"), "hi", "auto", "ar"));
    }

    [Fact]
    public void LostParagraphsAreNotAccepted()
    {
        const string inner = "[[null,null,\"en\"],[[[null,null,null,true,null,[[\"One.\"],[\"Two.\"]]]],\"ar\"],\"en\"]";
        Assert.Throws<ParagraphsLostException>(() => WebsiteResponseParser.Parse(Answer(inner), "One.\n\nTwo.", "auto", "ar"));
    }

    [Fact]
    public void RequestCarriesTheTextAndLanguages()
    {
        var request = WebsiteResponseParser.Request("Hi \"you\"", "auto", "ar");
        using var doc = JsonDocument.Parse(request);
        var call = doc.RootElement[0][0];
        Assert.Equal("MkEWBc", call[0].GetString());
        using var inner = JsonDocument.Parse(call[1].GetString()!);
        Assert.Equal("Hi \"you\"", inner.RootElement[0][0].GetString());
        Assert.Equal("auto", inner.RootElement[0][1].GetString());
        Assert.Equal("ar", inner.RootElement[0][2].GetString());
    }
}

public class TextCleanerTests
{
    [Fact]
    public void JoinsLinesBrokenInTheMiddleOfASentence()
    {
        var text = "The committee decided that the new policy would take\neffect at the beginning of next year, after the review.";
        Assert.Equal("The committee decided that the new policy would take effect at the beginning of next year, after the review.",
            TextCleaner.JoinBrokenLines(text));
    }

    [Fact]
    public void KeepsParagraphsListsAndShortLines()
    {
        var text = "Shopping list\n- milk\n- bread\n\nThe meeting ended early today.\nEveryone went home.";
        Assert.Equal(text, TextCleaner.JoinBrokenLines(text));
    }

    [Fact]
    public void JoinsWordsSplitWithAHyphen()
    {
        var text = "This agreement is recognised by every inter-\nnational organisation that signed it.";
        Assert.Equal("This agreement is recognised by every international organisation that signed it.", TextCleaner.JoinBrokenLines(text));
    }
}
