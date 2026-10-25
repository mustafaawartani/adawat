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

    private static async Task<TranslationResult> TranslateChunkAsync(string text, string source, string target, bool withExtras, CancellationToken token)
    {
        var url = new StringBuilder("https://translate.googleapis.com/translate_a/single?client=gtx&dj=1&ie=UTF-8&oe=UTF-8&hl=en");
        url.Append("&sl=").Append(Uri.EscapeDataString(source));
        url.Append("&tl=").Append(Uri.EscapeDataString(target));
        url.Append("&dt=t");
        if (withExtras) url.Append("&dt=bd&dt=rm");
        url.Append("&q=").Append(Uri.EscapeDataString(text));

        string json;
        try
        {
            using var response = await Http.GetAsync(url.ToString(), token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                throw new TranslationException("Google is limiting requests right now. Please try again in a minute.");
            if (!response.IsSuccessStatusCode)
                throw new TranslationException($"Google Translate answered with an error ({(int)response.StatusCode}).");
            json = await response.Content.ReadAsStringAsync(token);
        }
        catch (TranslationException)
        {
            throw;
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
            throw new TranslationException("Could not reach Google Translate. Check your internet connection.", ex);
        }

        return GoogleResponseParser.Parse(json, text, target);
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
