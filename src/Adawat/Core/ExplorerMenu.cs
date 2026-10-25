using System;
using Microsoft.Win32;

namespace Adawat.Core;

/// <summary>Adds "Extract text" entries to the right-click menu of image files (per user, no admin needed).</summary>
public static class ExplorerMenu
{
    // "image" covers every file type Windows sees as a picture; .webp is listed explicitly for older builds.
    private static readonly string[] FileClasses = { "image", ".webp" };

    private static readonly (string Key, string Label, string Argument)[] Verbs =
    {
        ("Adawat.ExtractText", "Extract text (Adawat)", "--ocr"),
        ("Adawat.ExtractTranslate", "Extract and translate text (Adawat)", "--ocr-translate"),
    };

    public static void Apply(bool enabled)
    {
        foreach (var fileClass in FileClasses)
        {
            foreach (var (key, label, argument) in Verbs)
            {
                var path = $@"Software\Classes\SystemFileAssociations\{fileClass}\shell\{key}";
                try
                {
                    if (enabled)
                    {
                        using var verb = Registry.CurrentUser.CreateSubKey(path);
                        verb.SetValue("", label);
                        verb.SetValue("MUIVerb", label);
                        verb.SetValue("Icon", $"\"{AppPaths.ExecutablePath}\",0");
                        using var command = verb.CreateSubKey("command");
                        command.SetValue("", $"\"{AppPaths.ExecutablePath}\" {argument} \"%1\"");
                    }
                    else
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(path, false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not update the Explorer menu ({path})", ex);
                }
            }
        }
    }
}
