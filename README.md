# Adawat · أدوات

A small, lightweight toolkit for Windows 10/11 that lives in the system tray.

| Tool | What it does |
|---|---|
| **Translator** | Translate selected text in *any* app (browsers, File Explorer, PDF readers, Office, chat apps…) with Google Translate. Shortcut, a small button next to your selection, or a typing box like the Chrome extension. |
| **Text from screen** | Drag around any part of the screen (pictures, videos, scanned PDFs) to read its text, then copy or translate it. Also in the right-click menu of image files. |
| **Touchpad gestures** | Browser-style gestures everywhere: two-finger swipe for **back / forward** and **pinch to zoom**, in File Explorer and other apps. |

## Install

1. Open the [latest release](https://github.com/mustafaawartani/adawat/releases/latest).
2. Download `Adawat-Setup-x.y.z.exe` and run it. No administrator rights needed.
   - Windows SmartScreen may say *"Windows protected your PC"* because the file is not code-signed: click **More info → Run anyway**.
3. Adawat starts, opens its settings once, and from then on starts with Windows.

To update, install the new setup over the old one. To remove it: *Settings → Apps → Installed apps → Adawat → Uninstall*.

## Default shortcuts

| Shortcut | Action |
|---|---|
| `Alt+1` | Translate the selected text (with nothing selected: open the translator box) |
| `Alt+2` | Read text from the screen |
| `Alt+3` | Open the translator box |
| Select text with the mouse | A small translate button appears next to it |
| Two-finger swipe right / left | Back / forward |
| Pinch / spread two fingers | Zoom out / in (in File Explorer: icon size) |

All shortcuts and behaviors can be changed in the settings (click the tray icon → Settings).

## Settings highlights

- Languages: from *Detect language* (English, Spanish, …) to Arabic by default. A second translation language, a fallback when the text is already in the target language, and favorites.
- Trigger: shortcut, translate button on selection, instant translation on selection, only while holding a key.
- Popup: text size, width, close on click outside, pin, dictionary, pronunciation, auto-copy, listen (Google voice).
- History of translations with search.
- Text from screen: language (uses the OCR languages installed in Windows), translate/copy right after, keep line breaks, Explorer menu.
- Gestures: fingers (2/3/4), sensitivity, direction, how to go back (mouse back button, Alt+arrows, browser keys), arrow animation, per-app lists, pause in full-screen games.
- Run as administrator (so the tools also work in admin windows), theme (light/dark/system), start with Windows.

## How it works

- Written in C# / .NET 8 with WPF and [WPF UI](https://github.com/lepoco/wpfui) (Windows 11 look). One small process, roughly 0% CPU while idle.
- **Selected text**: sends Ctrl+C to the active app and restores your clipboard afterwards; falls back to Windows UI Automation (and never sends Ctrl+C in terminals).
- **Translation**: the free Google Translate endpoint used by the browser extension. No account or API key.
- **OCR**: the `Windows.Media.Ocr` engine built into Windows (offline). Add languages in *Settings → Time & language → Language & region*.
- **Gestures**: reads the Precision Touchpad through the Raw Input API, recognizes swipes and pinches, then sends the mouse back/forward buttons or Ctrl+mouse wheel to the window under the pointer.

Settings, history and the log are in `%AppData%\Adawat`.

## Building

```powershell
dotnet test tests/Adawat.Tests
dotnet publish src/Adawat/Adawat.csproj -c Release -r win-x64 --self-contained true -o publish
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer\Adawat.iss
```

Every push to `main` builds the installer on GitHub Actions and publishes it as a release.
