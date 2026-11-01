using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Adawat.Translator;

public sealed record DictionaryEntry(string PartOfSpeech, IReadOnlyList<string> Terms);

public sealed record TranslationResult(
    string SourceText,
    string Translation,
    string SourceLanguage,
    string TargetLanguage,
    string? Transliteration,
    string? SourceTransliteration,
    IReadOnlyList<DictionaryEntry> Dictionary);

public class TranslationException : Exception
{
    public TranslationException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The website's service joined the paragraphs into one; this text is translated the classic way.</summary>
public sealed class ParagraphsLostException : TranslationException
{
    public ParagraphsLostException() : base("the paragraphs were not kept") { }
}

/// <summary>
/// Google Translate without an account or key. First the service behind translate.google.com itself
/// (the same translations as the website, whole text at once); if that fails or answers in a way
/// Adawat does not understand, the older free address used by browser extensions.
/// </summary>
public sealed class GoogleTranslator
{
    private const int MaxChunk = 1000; // keeps the request URL short even for Arabic text
    private const int MaxWebsiteChunk = 4500; // the website takes up to 5000 characters at once
    private static readonly HttpClient Http = CreateClient();

    /// <summary>Use the website's service first (see <see cref="TranslationEngine"/>).</summary>
    public bool UseWebsite { get; set; } = true;

    private static DateTime _websiteOffUntil;
    private static bool _loggedWebsite;

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
        return client;
    }

    /// <summary>Translates <paramref name="text"/>. Long text is split into pieces and joined again.</summary>
    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken token = default)
    {
        text = text.Trim();
        if (text.Length == 0) throw new TranslationException("Nothing to translate.");

        if (UseWebsite && DateTime.UtcNow >= _websiteOffUntil)
        {
            try
            {
                var result = await TranslateWithWebsiteAsync(text, source, target, token);
                if (!_loggedWebsite)
                {
                    _loggedWebsite = true;
                    LogInfo?.Invoke("Translating with the translate.google.com service");
                }
                return result;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (ParagraphsLostException)
            {
                // Only this text: go on below.
            }
            catch (Exception ex)
            {
                // Unofficial and may change: use the classic address for a while, then try again.
                _websiteOffUntil = DateTime.UtcNow.AddMinutes(10);
                _loggedWebsite = false;
                LogInfo?.Invoke($"The translate.google.com service did not work ({ex.Message}); using the classic address for 10 minutes");
            }
        }

        var chunks = TextChunker.Split(text, MaxChunk);
        var parts = new List<TranslationResult>();
        foreach (var chunk in chunks)
        {
            parts.Add(await TranslateChunkAsync(chunk.Text, source, target, withExtras: chunks.Count == 1, token));
        }
        return Combine(text, target, chunks, parts);
    }

    private static TranslationResult Combine(string text, string target, IReadOnlyList<TextChunker.Chunk> chunks, List<TranslationResult> parts)
    {
        if (parts.Count == 1) return parts[0] with { SourceText = text };

        var sb = new StringBuilder();
        for (int i = 0; i < parts.Count; i++)
        {
            sb.Append(parts[i].Translation);
            sb.Append(chunks[i].Separator);
        }
        var detected = parts.GroupBy(p => p.SourceLanguage).OrderByDescending(g => g.Count()).First().Key;
        return new TranslationResult(text, sb.ToString().TrimEnd(), detected, target, null, null, Array.Empty<DictionaryEntry>());
    }

    private const string WebsiteUrl =
        "https://translate.google.com/_/TranslateWebserverUi/data/batchexecute?rpcids=MkEWBc&source-path=%2F&hl=en&soc-app=1&soc-platform=1&soc-device=1&rt=c";

    /// <summary>The website's service: the whole text (up to 4500 characters a request) with its context.</summary>
    private static async Task<TranslationResult> TranslateWithWebsiteAsync(string text, string source, string target, CancellationToken token)
    {
        var chunks = TextChunker.Split(text, MaxWebsiteChunk);
        // A single word or short phrase: the dictionary and pronunciation come from the classic address,
        // asked at the same time. If that fails, the translation still shows.
        Task<TranslationResult?>? extras = chunks.Count == 1 && text.Length <= 60 && !text.Contains('\n')
            ? ExtrasAsync(text, source, target, token)
            : null;

        var parts = new List<TranslationResult>();
        foreach (var chunk in chunks) parts.Add(await WebsiteChunkAsync(chunk.Text, source, target, token));
        var result = Combine(text, target, chunks, parts);

        if (extras != null && await extras is { } more)
        {
            result = result with
            {
                Dictionary = more.Dictionary,
                Transliteration = result.Transliteration ?? more.Transliteration,
                SourceTransliteration = result.SourceTransliteration ?? more.SourceTransliteration,
            };
        }
        return result;
    }

    private static async Task<TranslationResult?> ExtrasAsync(string text, string source, string target, CancellationToken token)
    {
        try
        {
            return await TranslateChunkAsync(text, source, target, withExtras: true, token);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<TranslationResult> WebsiteChunkAsync(string text, string source, string target, CancellationToken token)
    {
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("f.req", WebsiteResponseParser.Request(text, source, target)) });
        using var response = await Http.PostAsync(WebsiteUrl, content, token);
        if (!response.IsSuccessStatusCode) throw new TranslationException($"the service answered {(int)response.StatusCode}");
        var body = await response.Content.ReadAsStringAsync(token);
        return WebsiteResponseParser.Parse(body, text, source, target);
    }

    /// <summary>
    /// Google's free addresses, best first. Each has its own request limit, so when one answers
    /// "too many requests" the next one is tried and the limited one is skipped for a while.
    /// The last one (the dictionary popup of Google's own Chrome extension) gives only the translation.
    /// </summary>
    private static readonly string[] Endpoints =
    {
        "https://translate.googleapis.com/translate_a/single?client=gtx",
        "https://translate.google.com/translate_a/single?client=gtx",
        "https://clients5.google.com/translate_a/t?client=dict-chrome-ex",
    };

    private static readonly DateTime[] LimitedUntil = new DateTime[Endpoints.Length];

    /// <summary>Where notes go (the app's log); this file is also used by the tests, which have no log.</summary>
    public static Action<string>? LogInfo { get; set; }

    private static async Task<TranslationResult> TranslateChunkAsync(string text, string source, string target, bool withExtras, CancellationToken token)
    {
        TranslationException? last = null;
        bool triedAny = false;
        for (int i = 0; i < Endpoints.Length; i++)
        {
            // Skip addresses that recently said "too many requests", unless all of them did.
            if (LimitedUntil[i] > DateTime.UtcNow && (i < Endpoints.Length - 1 || triedAny)) continue;
            triedAny = true;
            var url = BuildUrl(Endpoints[i], text, source, target, withExtras);
            try
            {
                using var response = await Http.GetAsync(url, token);
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Forbidden)
                {
                    LimitedUntil[i] = DateTime.UtcNow.AddMinutes(2);
                    LogInfo?.Invoke($"Google Translate address {i + 1} is limiting requests ({(int)response.StatusCode}); trying the next one");
                    last = new TranslationException("Google is limiting requests right now. Please try again in a minute.");
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    last = new TranslationException($"Google Translate answered with an error ({(int)response.StatusCode}).");
                    continue;
                }
                var json = await response.Content.ReadAsStringAsync(token);
                return Endpoints[i].Contains("/translate_a/t?")
                    ? GoogleResponseParser.ParseSimple(json, text, source, target)
                    : GoogleResponseParser.Parse(json, text, target);
            }
            catch (TranslationException ex)
            {
                last = ex;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                throw new TranslationException("Google Translate did not answer in time. Check your internet connection.", ex);
            }
            catch (HttpRequestException ex)
            {
                last = new TranslationException("Could not reach Google Translate. Check your internet connection.", ex);
            }
        }
        throw last ?? new TranslationException("Google is limiting requests right now. Please try again in a minute.");
    }

    private static string BuildUrl(string endpoint, string text, string source, string target, bool withExtras)
    {
        var url = new StringBuilder(endpoint);
        if (!endpoint.Contains("/translate_a/t?")) url.Append("&dj=1&ie=UTF-8&oe=UTF-8&hl=en");
        url.Append("&sl=").Append(Uri.EscapeDataString(source));
        url.Append("&tl=").Append(Uri.EscapeDataString(target));
        if (!endpoint.Contains("/translate_a/t?"))
        {
            url.Append("&dt=t");
            if (withExtras) url.Append("&dt=bd&dt=rm");
        }
        url.Append("&q=").Append(Uri.EscapeDataString(text));
        return url.ToString();
    }
}

/// <summary>
/// Builds the request for and reads the answer of the service behind translate.google.com
/// ("batchexecute", call MkEWBc). The answer is checked strictly: anything unexpected throws, and
/// the classic address is used instead.
/// </summary>
public static class WebsiteResponseParser
{
    /// <summary>The f.req form value for translating <paramref name="text"/>.</summary>
    public static string Request(string text, string source, string target)
    {
        var inner = JsonSerializer.Serialize(new object?[] { new object?[] { text, source, target, true }, new object?[] { null } });
        return JsonSerializer.Serialize(new object?[] { new object?[] { new object?[] { "MkEWBc", inner, null, "generic" } } });
    }

    public static TranslationResult Parse(string body, string sourceText, string source, string target)
    {
        var payload = FindPayload(body) ?? throw new TranslationException("no translation in the answer");
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            // root[1][0][0]: the translation; [5] its pieces (sentences), each piece [0] the text;
            // [3] true when the pieces are joined with spaces; [1] how the translation is read.
            var block = At(At(At(root, 1), 0), 0);
            var pieces = At(block, 5);
            if (pieces.ValueKind != JsonValueKind.Array || pieces.GetArrayLength() == 0) throw new TranslationException("no translation pieces");
            bool spaced = At(block, 3).ValueKind == JsonValueKind.True;
            var texts = new List<string>();
            foreach (var piece in pieces.EnumerateArray())
            {
                var t = At(piece, 0);
                if (t.ValueKind != JsonValueKind.String) throw new TranslationException("a translation piece is not text");
                texts.Add(t.GetString() ?? "");
            }
            var translation = string.Join(spaced ? " " : "", texts).Trim();
            if (translation.Length == 0) throw new TranslationException("empty translation");
            // Paragraphs must come back as paragraphs (the classic address keeps them).
            if (sourceText.Contains('\n') && !translation.Contains('\n')) throw new ParagraphsLostException();

            string? detected = new[] { Text(At(root, 2)), Text(At(At(root, 0), 2)), Text(At(At(root, 1), 3)) }
                .FirstOrDefault(code => code != null && code != "auto" && LooksLikeLanguage(code));
            // The detected language decides the "already in that language" fallback, so it must be known.
            if (detected == null)
            {
                if (source == "auto") throw new TranslationException("no detected language in the answer");
                detected = source;
            }
            return new TranslationResult(sourceText, translation, detected, target,
                NonEmpty(Text(At(block, 1))), NonEmpty(Text(At(At(root, 0), 0))), Array.Empty<DictionaryEntry>());
        }
        catch (JsonException ex)
        {
            throw new TranslationException("the answer could not be read", ex);
        }
    }

    /// <summary>The inner JSON of the ["wrb.fr","MkEWBc","…"] entry, from any of the answer's lines.</summary>
    private static string? FindPayload(string body)
    {
        if (body.StartsWith(")]}'", StringComparison.Ordinal)) body = body[4..];
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('[')) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (Search(doc.RootElement) is { } found) return found;
            }
            catch (JsonException)
            {
            }
        }
        return null;

        static string? Search(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Array) return null;
            if (element.GetArrayLength() >= 3 && Text(element[0]) == "wrb.fr" && Text(element[1]) == "MkEWBc" && Text(element[2]) is { Length: > 0 } inner)
                return inner;
            foreach (var child in element.EnumerateArray())
            {
                if (Search(child) is { } found) return found;
            }
            return null;
        }
    }

    /// <summary>"en", "ar", "zh-CN", "haw"…</summary>
    private static bool LooksLikeLanguage(string code) =>
        System.Text.RegularExpressions.Regex.IsMatch(code, "^[a-z]{2,3}(-[A-Za-z]{2,4})?$");

    private static JsonElement At(JsonElement element, int index) =>
        element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > index ? element[index] : default;

    private static string? Text(JsonElement element) => element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>
/// Text copied from PDFs, e-mails and narrow web columns breaks at the end of every line, in the
/// middle of sentences. Google translates every line on its own then, which gives broken, poor
/// translations; joining those lines first gives the translation the whole sentence.
/// </summary>
public static class TextCleaner
{
    public static string JoinBrokenLines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length < 2) return text;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (i == 0)
            {
                sb.Append(line);
                continue;
            }
            var previous = lines[i - 1].TrimEnd();
            var current = line.TrimStart();
            if (ShouldJoin(previous, current))
            {
                // "inter-\nnational" -> "international"; otherwise a space.
                if (previous.Length > 1 && previous[^1] == '-' && char.IsLetter(previous[^2]) && current.Length > 0 && char.IsLower(current[0]))
                {
                    while (sb.Length > 0 && char.IsWhiteSpace(sb[^1])) sb.Length--;
                    sb.Length--; // the hyphen
                }
                else
                {
                    while (sb.Length > 0 && char.IsWhiteSpace(sb[^1])) sb.Length--;
                    sb.Append(' ');
                }
                sb.Append(current);
            }
            else
            {
                sb.Append('\n').Append(line);
            }
        }
        return sb.ToString();
    }

    private const string SentenceEnds = ".!?:;؟。！？…\"”»)]";

    private static bool ShouldJoin(string previous, string current)
    {
        if (previous.Length == 0 || current.Length == 0) return false; // a blank line: a new paragraph
        if (SentenceEnds.Contains(previous[^1])) return false;
        if (previous.Length < 25) return false; // a heading or a short line on purpose
        // A list item or a new numbered point starts its own line.
        char first = current[0];
        if (first is '-' or '•' or '*' or '·' or '–' or '—' or '>' or '#') return false;
        int digits = 0;
        while (digits < current.Length && char.IsDigit(current[digits])) digits++;
        if (digits > 0 && digits < current.Length && current[digits] is '.' or ')') return false;
        return true;
    }
}

/// <summary>Reads the JSON returned by translate_a/single with dj=1.</summary>
public static class GoogleResponseParser
{
    public static TranslationResult Parse(string json, string sourceText, string target)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var translation = new StringBuilder();
            string? translit = null, sourceTranslit = null;
            if (root.TryGetProperty("sentences", out var sentences) && sentences.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sentences.EnumerateArray())
                {
                    if (s.TryGetProperty("trans", out var trans)) translation.Append(trans.GetString());
                    if (s.TryGetProperty("translit", out var tl) && tl.GetString() is { Length: > 0 } t) translit = t;
                    if (s.TryGetProperty("src_translit", out var stl) && stl.GetString() is { Length: > 0 } st) sourceTranslit = st;
                }
            }

            var dictionary = new List<DictionaryEntry>();
            if (root.TryGetProperty("dict", out var dict) && dict.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in dict.EnumerateArray())
                {
                    var pos = d.TryGetProperty("pos", out var p) ? p.GetString() ?? "" : "";
                    var terms = new List<string>();
                    if (d.TryGetProperty("terms", out var t) && t.ValueKind == JsonValueKind.Array)
                    {
                        terms.AddRange(t.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0));
                    }
                    if (terms.Count > 0) dictionary.Add(new DictionaryEntry(pos, terms));
                }
            }

            var src = root.TryGetProperty("src", out var srcEl) ? srcEl.GetString() ?? "" : "";
            return new TranslationResult(sourceText, translation.ToString(), src, target, translit, sourceTranslit, dictionary);
        }
        catch (JsonException ex)
        {
            throw new TranslationException("Google Translate sent an answer Adawat could not read.", ex);
        }
    }

    /// <summary>
    /// Reads the short answer of translate_a/t: ["text"] for a chosen source language, or
    /// [["text","en"]] (with the detected language) when the language is detected.
    /// </summary>
    public static TranslationResult ParseSimple(string json, string sourceText, string source, string target)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                throw new TranslationException("Google Translate sent an empty answer.");
            var first = root[0];
            string translation;
            string detected = source == "auto" ? "" : source;
            if (first.ValueKind == JsonValueKind.Array)
            {
                translation = first.GetArrayLength() > 0 ? first[0].GetString() ?? "" : "";
                if (first.GetArrayLength() > 1 && first[1].ValueKind == JsonValueKind.String) detected = first[1].GetString() ?? detected;
            }
            else
            {
                translation = first.GetString() ?? "";
            }
            return new TranslationResult(sourceText, translation, detected, target, null, null, Array.Empty<DictionaryEntry>());
        }
        catch (JsonException ex)
        {
            throw new TranslationException("Google Translate sent an answer Adawat could not read.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new TranslationException("Google Translate sent an answer Adawat could not read.", ex);
        }
    }
}

/// <summary>Splits long text at paragraph/sentence boundaries so each request stays small.</summary>
public static class TextChunker
{
    public readonly record struct Chunk(string Text, string Separator);

    public static IReadOnlyList<Chunk> Split(string text, int maxLength)
    {
        var result = new List<Chunk>();
        int start = 0;
        while (start < text.Length)
        {
            int remaining = text.Length - start;
            if (remaining <= maxLength)
            {
                result.Add(new Chunk(text.Substring(start), ""));
                break;
            }

            int end = FindBreak(text, start, maxLength);
            // Move whitespace before the break into the separator, so "\n\n" stays together.
            int trimmed = end;
            while (trimmed > start && char.IsWhiteSpace(text[trimmed - 1])) trimmed--;
            if (trimmed > start) end = trimmed;
            var piece = text.Substring(start, end - start);
            int next = end;
            while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
            var separator = text.Substring(end, next - end);
            result.Add(new Chunk(piece, separator.Contains('\n') ? separator : " "));
            start = next;
        }
        return result;
    }

    private static int FindBreak(string text, int start, int maxLength)
    {
        int limit = start + maxLength;
        int minimum = start + maxLength / 2;
        // Prefer a paragraph, then a sentence end, then any space.
        int index = text.LastIndexOf('\n', limit - 1, limit - minimum);
        if (index > minimum) return index;
        for (int i = limit - 1; i > minimum; i--)
        {
            if ((text[i] == '.' || text[i] == '!' || text[i] == '?' || text[i] == '؟' || text[i] == '。') && char.IsWhiteSpace(text[i + 1]))
                return i + 1;
        }
        index = text.LastIndexOf(' ', limit - 1, limit - minimum);
        return index > minimum ? index : limit;
    }
}
