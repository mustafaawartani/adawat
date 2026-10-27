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

public sealed class TranslationException : Exception
{
    public TranslationException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Google Translate through the free endpoint the Chrome extension uses (no account or key).
/// </summary>
public sealed class GoogleTranslator
{
    private const int MaxChunk = 1000; // keeps the request URL short even for Arabic text
    private static readonly HttpClient Http = CreateClient();

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

        var chunks = TextChunker.Split(text, MaxChunk);
        var parts = new List<TranslationResult>();
        foreach (var chunk in chunks)
        {
            parts.Add(await TranslateChunkAsync(chunk.Text, source, target, withExtras: chunks.Count == 1, token));
        }
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
