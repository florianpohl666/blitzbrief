# Blitztext for Windows

Experimental Windows MVP port of [cmagnussen/blitztext-app](https://github.com/cmagnussen/blitztext-app).

This is a native .NET 8 WPF tray app for speech-to-text workflows:

- **Blitztext**: record speech and transcribe it.
- **Blitztext+**: transcribe and improve the text.
- **Blitztext Ärger raus**: turn frustrated speech into a calmer message.
- **Blitztext :)**: add fitting emojis.

## Preview Notes

- Windows only.
- Bring your own OpenAI API key.
- No hosted Blitztext backend is included or used.
- Audio and rewrite text are sent directly from this PC to the OpenAI API.
- The API key is stored locally using Windows DPAPI, not in `settings.json`.
- Local Whisper/offline transcription is intentionally not included in this MVP.

## Build

Install the .NET 8 SDK, then run:

```powershell
dotnet restore
dotnet test
dotnet publish .\Blitztext.Windows\Blitztext.Windows.csproj -c Release -r win-x64 --self-contained false -o .\publish\Blitztext.Windows
```

Run `Blitztext.Windows.exe` from the publish folder. The app starts in the Windows notification area.

For a build that also works on machines without the .NET 8 Desktop Runtime installed:

```powershell
dotnet publish .\Blitztext.Windows\Blitztext.Windows.csproj -c Release -r win-x64 --self-contained true -o .\publish\Blitztext.Windows-self-contained
```

## Hotkeys

Defaults:

- `Ctrl+Shift+Space`: Blitztext
- `Ctrl+Shift+1`: Blitztext+
- `Ctrl+Shift+2`: Blitztext Ärger raus
- `Ctrl+Shift+3`: Blitztext :)

The app supports toggle mode and hold mode. Toggle mode starts on the first press and stops on the second press. Hold mode records while the hotkey is held and processes when it is released.

## Attribution

The product idea, workflow names, and prompt behavior are adapted from the MIT-licensed macOS preview at `cmagnussen/blitztext-app`. This Windows codebase is a native rebuild because the original app depends on SwiftUI, AppKit, macOS Keychain, CoreGraphics paste events, and WhisperKit/CoreML.
