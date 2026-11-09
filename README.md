# Adawat · أدوات

A small, lightweight toolkit for Windows 10/11 that lives in the system tray.

| Tool | What it does |
|---|---|
| **Translator** | Translate selected text in *any* app (browsers, File Explorer, PDF readers, Office, chat apps…) with Google Translate. Shortcut, a small button next to your selection, or a typing box like the Chrome extension. |
| **Text from screen** | Drag around any part of the screen (pictures, videos, scanned PDFs) to read its text, then copy or translate it. Also in the right-click menu of image files. |
| **Touchpad gestures** | Browser-style gestures everywhere: two-finger swipe for **back / forward**, and **pinch to zoom the screen** like Chrome (a visual zoom around the pointer, nothing in the app changes size). |
| **Picture zoom** | Like the Photo Zoom extension, but in every app: rest the pointer on a picture to see it big, `Alt+S` saves it to Downloads, `Alt+C` copies it. |
| **Change keys** | Make any key act as another, everywhere. For example swap Left Ctrl and Left Alt, so copy becomes `Alt+C`. |
| **Quick note** | A small note box one shortcut away, ready to type in. Notes are saved automatically and kept as tilted tabs on the side, newest first. |
| **Stay awake** | Keep the PC working for 30 minutes, an hour, until a time or until you turn it off, **also with the lid closed** (Wi-Fi stays on, downloads go on). Then it can sleep again. |

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
| `Alt+4` | Open the quick note box (press it again to hide it) |
| Select text with the mouse | A small translate button appears next to it |
| Two-finger swipe right / left | Back / forward |
| Spread / pinch two fingers | Zoom the screen in / back out, like Chrome |
| Slide two fingers while zoomed | Move around the zoomed screen in any direction; at the edge of the screen the page keeps scrolling, like Chrome. The mouse pointer stays inside the zoomed view and does not move it |
| Rest the pointer on a picture | Show it big; while it is open `Alt+S` saves it to Downloads and `Alt+C` copies it |

| Click the tray icon | Settings (right-click for the translator, Stay awake and more) |

All shortcuts and behaviors can be changed in the settings (click the tray icon).

## Settings highlights

- Languages: from *Detect language* (English, Spanish, …) to Arabic by default. A second translation language, a fallback when the text is already in the target language, and favorites.
- Trigger: shortcut, translate button on selection (stays while the text is selected, or hides after a few seconds), instant translation on selection, only while holding a key.
- Look of the translator: **Glass** (frosted, see-through, with "how see-through") or **Color** (solid), in the app color, one of 8 colors or 8 two-color blends (Ocean, Sunset, Aurora, Berry, Sky, Peach, Forest, Dusk).
- Translation service: the same service as translate.google.com (default, falls back to the classic one by itself) or the classic address. Lines broken in the middle of sentences (PDFs, web columns) are joined before translating.
- Popup: text size, width, close on click outside, pin, dictionary, pronunciation, auto-copy, listen (Google voice), show or hide "Open in Google Translate". Searchable language menus.
- History of translations with search.
- Text from screen: language (uses the OCR languages installed in Windows), translate/copy right after, keep line breaks, Explorer menu.
- Gestures: fingers (2/3/4), sensitivity, direction, how to go back (mouse back button, Alt+arrows, browser keys), arrow animation, per-app lists, pause in full-screen games. A swipe does nothing (and shows no arrow) when the app's own back/forward button is greyed out.
- Pinch: screen zoom like Chrome (zoom speed from very slow to fast, maximum zoom from 1.25x to 10x, the pointer stays inside the zoomed view or pushes it along, keep scrolling at the edge, smooth edges or blocky pixels, optional settling on whole zoom levels) or app zoom (Ctrl+wheel, Ctrl+Plus/Minus). While your fingers are on the touchpad over an app where the screen zoom works, Windows' own pinch (icon and text size) is paused for that moment; your Windows setting is never changed, and apps in the "pinching does nothing" list keep their own pinch.
- Picture zoom: small pictures open enlarged (at least 2.5x their size on the page), big ones at full size. Save/copy shortcuts (only taken while a preview is open, with a notification at the bottom of the screen when done), delay, only while holding a key, minimum size, per-app list. The log notes why a picture did not open.
- Change keys: any key to any other key (or off), a one-click "Swap Ctrl and Alt" (left and right keys), per-app list. Click a key box and press the key, even Windows, Alt or Caps Lock. When Alt works as Ctrl, chosen Alt shortcuts stay Alt shortcuts (Alt + Tab, Alt + F4, Alt + Esc and Alt + Space by default; also Alt + Shift, arrows, Enter, numbers or letters).
- Quick note: shortcut, close when clicking elsewhere (or keep it open with the pin), tabs on the left or right, color (the app color or any translator color), text size and font. Right-click a tab to delete the note; `Ctrl+N` makes a new note, `Esc` hides the box.
- Stay awake: how long (30 min to 8 h, another length, until a time of day, until turned off), keep working with the lid closed, keep the screen on, sleep when the time is up if the lid is closed. Also in the tray menu.
- Run as administrator (so the tools also work in admin windows), theme (light/dark/system) and accent color, start with Windows.

## How it works

- Written in C# / .NET 8 with WPF and [WPF UI](https://github.com/lepoco/wpfui) (Windows 11 look). One small process, roughly 0% CPU while idle.
- **Selected text**: sends Ctrl+C to the active app and restores your clipboard afterwards; falls back to Windows UI Automation (and never sends Ctrl+C in terminals).
- **Translation**: first the service behind translate.google.com itself (the same translations as the website, long text in one piece); if it fails, the free endpoints used by the browser extensions. No account or API key. When Google limits one address, the next one is used.
- **OCR**: the `Windows.Media.Ocr` engine built into Windows (offline). Add languages in *Settings → Time & language → Language & region*.
- **Gestures**: reads the Precision Touchpad through the Raw Input API, recognizes swipes and pinches, then sends the mouse back/forward buttons to the window under the pointer. Pinch uses the Windows magnification API (the one behind Windows Magnifier) to magnify the screen, drawn on its own thread in step with the screen refresh and smoothed so touchpad noise does not shake the picture.
- **Picture zoom**: finds the picture under the pointer with UI Automation. Picture files in File Explorer and on the desktop open from disk at full quality, pictures on web pages are downloaded from their own address, asking the site for the full-size version first (YouTube and Google profile pictures and thumbnails, Twitter/X, Wikipedia, Reddit, Imgur, Amazon, Pinterest, WordPress, resizing CDNs). Only pictures with no address are enlarged from the screen.
- **Quick note**: a small window with one tab per note (the first words of the note, written sideways to save width). Notes save by themselves half a second after you stop typing and whenever the box is hidden; empty notes are not kept. The notes, the last open note and the size and place of the box are in `notes.json`. Arabic and other right-to-left notes start from the right.
- **Stay awake**: a power request keeps the PC from sleeping (like PowerToys Awake). For the lid, Windows' "When I close the lid" setting of the current power plan is set to "Do nothing" and put back exactly as it was afterwards (also after a crash or restart).
- **Change keys**: a low-level keyboard hook (like PowerToys Keyboard Manager) swaps the keys as you type. Nothing is written to the registry and no restart is needed.

Settings, history, notes and the log are in `%AppData%\Adawat`.

## Building

```powershell
dotnet test tests/Adawat.Tests
dotnet publish src/Adawat/Adawat.csproj -c Release -r win-x64 --self-contained true -o publish
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer\Adawat.iss
```

Every push to `main` builds the installer on GitHub Actions and publishes it as a release.
