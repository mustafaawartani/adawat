using System;
using System.Collections.Generic;
using System.Linq;

namespace Adawat.Translator;

public sealed record Language(string Code, string Name);

/// <summary>The languages Google Translate supports, by Google's codes.</summary>
public static class Languages
{
    public const string Auto = "auto";

    public static readonly IReadOnlyList<Language> All = new[]
    {
        new Language("af", "Afrikaans"), new Language("sq", "Albanian"), new Language("am", "Amharic"),
        new Language("ar", "Arabic"), new Language("hy", "Armenian"), new Language("as", "Assamese"),
        new Language("ay", "Aymara"), new Language("az", "Azerbaijani"), new Language("bm", "Bambara"),
        new Language("eu", "Basque"), new Language("be", "Belarusian"), new Language("bn", "Bengali"),
        new Language("bho", "Bhojpuri"), new Language("bs", "Bosnian"), new Language("bg", "Bulgarian"),
        new Language("ca", "Catalan"), new Language("ceb", "Cebuano"), new Language("ny", "Chichewa"),
        new Language("zh-CN", "Chinese (Simplified)"), new Language("zh-TW", "Chinese (Traditional)"),
        new Language("co", "Corsican"), new Language("hr", "Croatian"), new Language("cs", "Czech"),
        new Language("da", "Danish"), new Language("dv", "Dhivehi"), new Language("doi", "Dogri"),
        new Language("nl", "Dutch"), new Language("en", "English"), new Language("eo", "Esperanto"),
        new Language("et", "Estonian"), new Language("ee", "Ewe"), new Language("tl", "Filipino"),
        new Language("fi", "Finnish"), new Language("fr", "French"), new Language("fy", "Frisian"),
        new Language("gl", "Galician"), new Language("ka", "Georgian"), new Language("de", "German"),
        new Language("el", "Greek"), new Language("gn", "Guarani"), new Language("gu", "Gujarati"),
        new Language("ht", "Haitian Creole"), new Language("ha", "Hausa"), new Language("haw", "Hawaiian"),
        new Language("iw", "Hebrew"), new Language("hi", "Hindi"), new Language("hmn", "Hmong"),
        new Language("hu", "Hungarian"), new Language("is", "Icelandic"), new Language("ig", "Igbo"),
        new Language("ilo", "Ilocano"), new Language("id", "Indonesian"), new Language("ga", "Irish"),
        new Language("it", "Italian"), new Language("ja", "Japanese"), new Language("jw", "Javanese"),
        new Language("kn", "Kannada"), new Language("kk", "Kazakh"), new Language("km", "Khmer"),
        new Language("rw", "Kinyarwanda"), new Language("gom", "Konkani"), new Language("ko", "Korean"),
        new Language("kri", "Krio"), new Language("ku", "Kurdish (Kurmanji)"), new Language("ckb", "Kurdish (Sorani)"),
        new Language("ky", "Kyrgyz"), new Language("lo", "Lao"), new Language("la", "Latin"),
        new Language("lv", "Latvian"), new Language("ln", "Lingala"), new Language("lt", "Lithuanian"),
        new Language("lg", "Luganda"), new Language("lb", "Luxembourgish"), new Language("mk", "Macedonian"),
        new Language("mai", "Maithili"), new Language("mg", "Malagasy"), new Language("ms", "Malay"),
        new Language("ml", "Malayalam"), new Language("mt", "Maltese"), new Language("mi", "Maori"),
        new Language("mr", "Marathi"), new Language("mni-Mtei", "Meiteilon (Manipuri)"), new Language("lus", "Mizo"),
        new Language("mn", "Mongolian"), new Language("my", "Myanmar (Burmese)"), new Language("ne", "Nepali"),
        new Language("no", "Norwegian"), new Language("or", "Odia (Oriya)"), new Language("om", "Oromo"),
        new Language("ps", "Pashto"), new Language("fa", "Persian"), new Language("pl", "Polish"),
        new Language("pt", "Portuguese"), new Language("pa", "Punjabi"), new Language("qu", "Quechua"),
        new Language("ro", "Romanian"), new Language("ru", "Russian"), new Language("sm", "Samoan"),
        new Language("sa", "Sanskrit"), new Language("gd", "Scots Gaelic"), new Language("nso", "Sepedi"),
        new Language("sr", "Serbian"), new Language("st", "Sesotho"), new Language("sn", "Shona"),
        new Language("sd", "Sindhi"), new Language("si", "Sinhala"), new Language("sk", "Slovak"),
        new Language("sl", "Slovenian"), new Language("so", "Somali"), new Language("es", "Spanish"),
        new Language("su", "Sundanese"), new Language("sw", "Swahili"), new Language("sv", "Swedish"),
        new Language("tg", "Tajik"), new Language("ta", "Tamil"), new Language("tt", "Tatar"),
        new Language("te", "Telugu"), new Language("th", "Thai"), new Language("ti", "Tigrinya"),
        new Language("ts", "Tsonga"), new Language("tr", "Turkish"), new Language("tk", "Turkmen"),
        new Language("ak", "Twi"), new Language("uk", "Ukrainian"), new Language("ur", "Urdu"),
        new Language("ug", "Uyghur"), new Language("uz", "Uzbek"), new Language("vi", "Vietnamese"),
        new Language("cy", "Welsh"), new Language("xh", "Xhosa"), new Language("yi", "Yiddish"),
        new Language("yo", "Yoruba"), new Language("zu", "Zulu"),
    };

    private static readonly HashSet<string> RightToLeft = new(StringComparer.OrdinalIgnoreCase)
    {
        "ar", "iw", "he", "fa", "ur", "ps", "sd", "ug", "yi", "dv", "ckb",
    };

    public static string NameOf(string? code)
    {
        if (string.IsNullOrEmpty(code)) return "";
        if (code == Auto) return "Detect language";
        var match = All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
                    ?? All.FirstOrDefault(l => string.Equals(l.Code, code.Split('-')[0], StringComparison.OrdinalIgnoreCase));
        if (match == null && string.Equals(code, "he", StringComparison.OrdinalIgnoreCase)) return "Hebrew";
        return match?.Name ?? code;
    }

    public static bool IsRightToLeft(string? code) =>
        !string.IsNullOrEmpty(code) && RightToLeft.Contains(code.Split('-')[0]);

    /// <summary>True if the text starts (after neutral characters) with a right-to-left letter.</summary>
    public static bool LooksRightToLeft(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var ch in text)
        {
            if (ch >= 0x0590 && ch <= 0x08FF || ch >= 0xFB1D && ch <= 0xFEFC) return true;
            if (char.IsLetter(ch)) return false;
        }
        return false;
    }

    /// <summary>Favorites first, then the rest alphabetically.</summary>
    public static IReadOnlyList<Language> Ordered(IEnumerable<string> favorites)
    {
        var fav = favorites
            .Select(code => All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)))
            .Where(l => l != null)
            .Cast<Language>()
            .Distinct()
            .ToList();
        return fav.Concat(All.Where(l => !fav.Contains(l))).ToList();
    }

    /// <summary>Compares language codes, treating Hebrew's "he" and Google's "iw" as the same.</summary>
    public static bool Same(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        static string Norm(string c) => c.Equals("he", StringComparison.OrdinalIgnoreCase) ? "iw" : c;
        return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
    }
}
