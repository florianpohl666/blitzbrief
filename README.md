# Blitztext for Windows

Blitztext for Windows is an experimental native Windows app for turning speech into text. It is a .NET 8 WPF tray app inspired by the MIT-licensed macOS preview [cmagnussen/blitztext-app](https://github.com/cmagnussen/blitztext-app), but rebuilt for Windows.

## Funktionen

- **Blitztext**: Sprache aufnehmen und transkribieren.
- **Text verbessern**: Sprache aufnehmen, transkribieren und als saubereren Text formulieren.
- **Ärger beruhigen**: emotional gesprochene Gedanken in eine klare, respektvolle Nachricht umwandeln.
- **Emoji ergänzen**: Text transkribieren und passende Emojis ergänzen.
- **Desktop-Leiste**: kleine schwebende Leiste mit drei Aufnahme-Buttons.
- **Hotkeys**: frei konfigurierbare globale Tastenkombinationen.
- **Mikrofonwahl**: Aufnahmegerät in den Einstellungen auswählen.

## Wichtig

- Windows only.
- Kein eigener Server, keine Serveradresse.
- Jeder Benutzer trägt seinen eigenen OpenAI API Key lokal in der App ein.
- Audio und Text werden direkt vom eigenen PC an die OpenAI API gesendet.
- Der OpenAI API Key wird lokal per Windows DPAPI unter dem Benutzerkonto gespeichert.
- Der API Key wird nicht in `settings.json`, nicht im Repo und nicht im Build-Output gespeichert.
- Lokale/offline Transkription ist in diesem MVP noch nicht enthalten.

## Voraussetzungen

- Windows 10/11
- Für Entwicklung/Build: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Für die Nutzung: eigener OpenAI API Key mit Zugriff auf Audio-Transkription und Chat-Completions/Chat-Modelle

## Starten als Benutzer

Wenn bereits ein Build vorhanden ist:

```powershell
.\Start-Blitztext.cmd
```

Oder direkt:

```powershell
.\publish\Blitztext.Windows-self-contained\Blitztext.Windows.exe
```

Beim ersten Start:

1. OpenAI API Key in den Einstellungen speichern.
2. Mikrofon auswählen.
3. Entweder Desktop-Leiste verwenden oder Hotkeys konfigurieren.

## Bauen aus dem Repo

```powershell
dotnet restore
dotnet test
dotnet publish .\Blitztext.Windows\Blitztext.Windows.csproj -c Release -r win-x64 --self-contained true -o .\publish\Blitztext.Windows-self-contained
```

Danach starten:

```powershell
.\publish\Blitztext.Windows-self-contained\Blitztext.Windows.exe
```

Optional eine Desktop-Verknüpfung erstellen:

```powershell
.\Install-Blitztext-Shortcut.cmd
```

## Hotkeys

Standardwerte:

- `Ctrl+Shift+Space`: Blitztext
- `Ctrl+Shift+1`: Text verbessern
- `Ctrl+Shift+2`: Ärger beruhigen
- `Ctrl+Shift+3`: Emoji ergänzen

In den Einstellungen können Hotkeys neu aufgenommen werden: ins Feld klicken, gewünschte Kombination drücken, speichern.


## Lokale Daten

Blitztext legt lokale Benutzerdaten unter `%AppData%\Blitztext\` ab, zum Beispiel:

- `settings.json`: App-Einstellungen ohne API Key
- `openai.key`: DPAPI-geschützter API-Key-Speicher
- `blitztext.log`: lokales Diagnose-Log


## Attribution

Die Idee, Workflow-Namen und Prompt-Richtung sind vom macOS-Projekt [cmagnussen/blitztext-app](https://github.com/cmagnussen/blitztext-app) inspiriert. Diese Windows-Codebasis ist ein nativer Neubau, weil das Original SwiftUI, AppKit, macOS Keychain, CoreGraphics-Paste-Events und WhisperKit/CoreML verwendet.
