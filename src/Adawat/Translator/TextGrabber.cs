using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using Adawat.Core;
using Adawat.Interop;

namespace Adawat.Translator;

/// <summary>
/// Gets the selected text from whatever app has focus: first by sending Ctrl+C (and putting
/// the user's clipboard back afterwards), then through Windows accessibility (UI Automation).
/// In File Explorer it returns the names of the selected files.
/// </summary>
public sealed class TextGrabber
{
    private readonly TranslatorSettings _settings;

    public TextGrabber(TranslatorSettings settings) => _settings = settings;

    public async Task<string?> GrabAsync(string processName)
    {
        // The shortcut keys (e.g. Alt) are usually still held; Ctrl+C would become Ctrl+Alt+C.
        await WaitForModifiersReleasedAsync(600);
        InputSender.ReleaseModifiers();

        bool clipboardAllowed = _settings.CaptureMethod != CaptureMethod.AccessibilityOnly
                                && !WindowInfo.ListContains(_settings.NoCopyShortcutApps, processName);
        bool accessibilityAllowed = _settings.CaptureMethod != CaptureMethod.ClipboardOnly;

        string? text = null;
        if (clipboardAllowed) text = await ViaClipboardAsync();
        if (string.IsNullOrWhiteSpace(text) && accessibilityAllowed) text = await ViaAccessibilityAsync();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static async Task WaitForModifiersReleasedAsync(int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (InputSender.AnyModifierDown() && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(15);
        }
    }

    // ---- Clipboard -------------------------------------------------------------

    private static async Task<string?> ViaClipboardAsync()
    {
        var backup = await SaveClipboardAsync();
        uint sequence = Native.GetClipboardSequenceNumber();
        InputSender.CopyShortcut();

        var sw = Stopwatch.StartNew();
        while (Native.GetClipboardSequenceNumber() == sequence && sw.ElapsedMilliseconds < 650)
        {
            await Task.Delay(15);
        }
        if (Native.GetClipboardSequenceNumber() == sequence) return null; // nothing was selected

        await Task.Delay(30); // let the app finish writing all formats
        string? text = null;
        for (int attempt = 0; attempt < 6 && text == null; attempt++)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    text = Clipboard.GetText();
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    text = FileNamesToText(Clipboard.GetFileDropList());
                }
                break;
            }
            catch (COMException)
            {
                await Task.Delay(40);
            }
        }

        await RestoreClipboardAsync(backup);
        return text;
    }

    private static string FileNamesToText(StringCollection files)
    {
        var names = new List<string>();
        foreach (string? path in files)
        {
            if (string.IsNullOrEmpty(path)) continue;
            var name = Directory.Exists(path) ? Path.GetFileName(path.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(path);
            names.Add(CleanFileName(name));
        }
        return string.Join("\n", names);
    }

    private static string CleanFileName(string name) => name.Replace('_', ' ').Replace('.', ' ').Trim();

    /// <summary>Removes a short extension such as ".pdf" from a displayed item name.</summary>
    private static string StripExtension(string name)
    {
        var ext = Path.GetExtension(name);
        return ext.Length is > 1 and <= 6 && ext.Skip(1).All(char.IsLetterOrDigit) ? name[..^ext.Length] : name;
    }

    private static async Task<Dictionary<string, object>?> SaveClipboardAsync()
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var data = Clipboard.GetDataObject();
                if (data == null) return null;
                var saved = new Dictionary<string, object>();
                foreach (var format in data.GetFormats(false))
                {
                    try
                    {
                        var value = data.GetData(format, false);
                        if (value != null) saved[format] = value;
                    }
                    catch
                    {
                        // Some formats (e.g. delayed or private ones) cannot be read; skip them.
                    }
                }
                return saved;
            }
            catch (COMException)
            {
                await Task.Delay(40);
            }
        }
        return null;
    }

    private static async Task RestoreClipboardAsync(Dictionary<string, object>? saved)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (saved == null || saved.Count == 0)
                {
                    Clipboard.Clear();
                }
                else
                {
                    var data = new DataObject();
                    foreach (var (format, value) in saved)
                    {
                        try { data.SetData(format, value); } catch { }
                    }
                    Clipboard.SetDataObject(data, true);
                }
                return;
            }
            catch (COMException)
            {
                await Task.Delay(50);
            }
            catch (Exception ex)
            {
                Log.Error("Could not restore the clipboard", ex);
                return;
            }
        }
    }

    // ---- UI Automation -----------------------------------------------------------

    private static async Task<string?> ViaAccessibilityAsync()
    {
        var task = Task.Run(ReadSelectionWithAutomation);
        var finished = await Task.WhenAny(task, Task.Delay(800));
        return finished == task ? task.Result : null;
    }

    private static string? ReadSelectionWithAutomation()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return null;

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
            {
                var ranges = ((TextPattern)pattern).GetSelection();
                var text = string.Join("\n", ranges.Select(r => r.GetText(4000)));
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }

            // File Explorer and other lists: names of the selected items.
            var type = element.Current.ControlType;
            if (type == ControlType.ListItem || type == ControlType.DataItem || type == ControlType.TreeItem)
            {
                var walker = TreeWalker.ControlViewWalker;
                var parent = walker.GetParent(element);
                for (int i = 0; i < 5 && parent != null; i++)
                {
                    if (parent.TryGetCurrentPattern(SelectionPattern.Pattern, out var sel))
                    {
                        var items = ((SelectionPattern)sel).Current.GetSelection();
                        if (items.Length > 0)
                            return string.Join("\n", items.Select(it => CleanFileName(StripExtension(it.Current.Name))));
                        break;
                    }
                    parent = walker.GetParent(parent);
                }
                return CleanFileName(StripExtension(element.Current.Name));
            }
        }
        catch (Exception ex)
        {
            Log.Info($"UI Automation could not read the selection: {ex.Message}");
        }
        return null;
    }
}
