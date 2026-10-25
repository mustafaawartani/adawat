using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Adawat.Core;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Adawat.Translator;

/// <summary>Reads text aloud with Google's voice, falling back to the voices installed in Windows.</summary>
public sealed class SpeechPlayer : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly MediaPlayer _player = new();
    private CancellationTokenSource? _cts;

    static SpeechPlayer()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _player.Pause(); } catch { }
    }

    public async Task SpeakAsync(string text, string language)
    {
        Stop();
        text = text.Trim();
        if (text.Length == 0) return;
        var cts = _cts = new CancellationTokenSource();

        try
        {
            var list = new MediaPlaybackList();
            foreach (var chunk in TextChunker.Split(text.Length > 1200 ? text[..1200] : text, 180))
            {
                var bytes = await DownloadAsync(chunk.Text, language, cts.Token);
                var stream = new MemoryStream(bytes).AsRandomAccessStream();
                list.Items.Add(new MediaPlaybackItem(MediaSource.CreateFromStream(stream, "audio/mpeg")));
            }
            if (cts.IsCancellationRequested) return;
            _player.Source = list;
            _player.Play();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Info($"Google voice unavailable ({ex.Message}); using a Windows voice");
            await SpeakWithWindowsAsync(text, language, cts.Token);
        }
    }

    private static async Task<byte[]> DownloadAsync(string text, string language, CancellationToken token)
    {
        var lang = Uri.EscapeDataString(language == Languages.Auto ? "en" : language);
        var q = Uri.EscapeDataString(text);
        var urls = new[]
        {
            $"https://translate.googleapis.com/translate_tts?client=gtx&ie=UTF-8&tl={lang}&q={q}",
            $"https://translate.google.com/translate_tts?client=tw-ob&ie=UTF-8&tl={lang}&q={q}",
        };
        Exception? last = null;
        foreach (var url in urls)
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url, token);
                if (bytes.Length > 100) return bytes;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }
        throw last ?? new IOException("No audio received");
    }

    private async Task SpeakWithWindowsAsync(string text, string language, CancellationToken token)
    {
        try
        {
            using var synth = new SpeechSynthesizer();
            foreach (var voice in SpeechSynthesizer.AllVoices)
            {
                if (voice.Language.StartsWith(language, StringComparison.OrdinalIgnoreCase))
                {
                    synth.Voice = voice;
                    break;
                }
            }
            var stream = await synth.SynthesizeTextToStreamAsync(text);
            if (token.IsCancellationRequested) return;
            _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _player.Play();
        }
        catch (Exception ex)
        {
            Log.Error("Text to speech failed", ex);
        }
    }

    public void Dispose()
    {
        Stop();
        _player.Dispose();
    }
}
