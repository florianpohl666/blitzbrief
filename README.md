# BlitzBrief for Windows

BlitzBrief for Windows is an experimental native Windows app for turning speech into text. It is a .NET 8 WPF tray app inspired by the MIT-licensed macOS preview [cmagnussen/BlitzBrief-app](https://github.com/cmagnussen/BlitzBrief-app), but rebuilt for Windows.

## Funktionen

- **BlitzBrief**: Sprache aufnehmen und transkribieren.
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
.\Start-BlitzBrief.cmd
```

Oder direkt:

```powershell
.\publish\BlitzBrief.Windows-self-contained\BlitzBrief.Windows.exe
```

Beim ersten Start:

1. OpenAI API Key in den Einstellungen speichern.
2. Mikrofon auswählen.
3. Entweder Desktop-Leiste verwenden oder Hotkeys konfigurieren.

## Bauen aus dem Repo

```powershell
dotnet restore
dotnet test
dotnet publish .\BlitzBrief.Windows\BlitzBrief.Windows.csproj -c Release -r win-x64 --self-contained true -o .\publish\BlitzBrief.Windows-self-contained
```

Danach starten:

```powershell
.\publish\BlitzBrief.Windows-self-contained\BlitzBrief.Windows.exe
```

Optional eine Desktop-Verknüpfung erstellen:

```powershell
.\Install-BlitzBrief-Shortcut.cmd
```

## Hotkeys

Standardwerte:

- `Ctrl+Shift+Space`: BlitzBrief
- `Ctrl+Shift+1`: Text verbessern
- `Ctrl+Shift+2`: Ärger beruhigen
- `Ctrl+Shift+3`: Emoji ergänzen

In den Einstellungen können Hotkeys neu aufgenommen werden: ins Feld klicken, gewünschte Kombination drücken, speichern.


