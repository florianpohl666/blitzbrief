---
description: Generiert die BlitzBrief-Dokumentation (technisch, Anwender, Merkblatt) neu aus dem aktuellen Code.
---

Erzeuge die BlitzBrief-Dokumentation in `docs/` **neu** auf Basis des **aktuellen Codes**. Nur auf Zuruf ausführen (nicht automatisch pro Release).

## Vorgehen

1. **Stand ermitteln:** aktuelles Datum und `git rev-parse --short HEAD`. Wenn der Working-Tree uncommittete Änderungen hat, im Stand-Header „+ Working-Tree" vermerken.

2. **Code lesen (Quelle der Wahrheit – nichts erfinden, alles gegen den Code prüfen):**
   - `BlitzBrief.Core/Models/WorkflowType.cs` – Modi + DisplayNames
   - `BlitzBrief.Core/Workflow/WorkflowRunner.cs` – Pipeline, Modell-/Temperatur-Wahl, `TranscriptionModelFor`, Reihenfolge der Verarbeitung
   - `BlitzBrief.Core/PromptBuilder.cs` – Whisper-/Rewrite-Prompts, Stile (`TextTone`), `UsesJornCommands`
   - `BlitzBrief.Core/TranscriptionQualityService.cs` – Kommando-Normalisierung & -Ersetzung (Regex, Kommando-Tabelle, Kategorien), `NormalizeSymbols` (§/€), Qualitäts-Schutz (`ShouldRejectRecording`, `IsLikelyArtifact`, `IsPromptEcho`, `IsImplausiblyFast` inkl. Schwellen)
   - `BlitzBrief.Core/SentenceContext.cs` und `BlitzBrief.Core/SmartInsert.cs` – Kontext-Satz & Smart-Insert (Leerzeichen-/Punkt-Regeln)
   - `BlitzBrief.Core/Settings/AppSettings.cs` – Defaults, Default-Hotkeys
   - `BlitzBrief.Core/OpenAI/RealtimeTranscriber.cs` – Realtime-Wire-Format
   - `BlitzBrief.Windows/Platform/CursorContextReader.cs` – UIA + Office-COM, beide Cursor-Seiten
   - `BlitzBrief.Windows/TrayController.cs` – Auslöser, Kontext-Erfassung, `ComposeInsertText`
   - `BlitzBrief.Windows/Platform/ClipboardPasteService.cs` – Einfügen
   - `BlitzBrief.Windows/SettingsWindow.xaml(.cs)`, `FloatingToolbarWindow.xaml(.cs)` – UI/Bedienung
   - Relevante Memories (Realtime-/Kontext-Befunde), falls vorhanden.

3. **Dokus neu schreiben** (gleiche Aufteilung/Zielgruppen beibehalten, Stand-Header oben aktualisieren):
   - `docs/technische-doku.md` – Architektur, Gesamtablauf, Realtime/Batch, Qualitäts-Schutz, Whisper-Prompt, Modi & Pipelines (Tabelle + Diagramm), Jörn-Kommandos (Regex + Tabelle), Symbol-Normalisierung, Kontext/SmartInsert, Einfügen, Einstellungen. **Mermaid**-Diagramme für Architektur und Pipelines.
   - `docs/anwender-doku.md` – Einrichtung, Bedienung, Modus-Vergleichstabelle, Kommandos, Kontext-Modus, Einstellungen, Troubleshooting. Mermaid wo es hilft.
   - `docs/kommando-merkblatt.md` – Kommando-Tabelle, Modus-Spickzettel, Do's/Don'ts, Entscheidungsbaum (Mermaid). Eine Seite.
   - `docs/README.md` – Index mit Stand-Header.

4. **Korrektheit sichern:** Kommando-Tabelle, Regex-Beschreibungen, Modellnamen, Temperaturen, Schwellenwerte und Default-Hotkeys exakt aus dem Code übernehmen. Neue Modi/Stile, die seit der letzten Generierung dazugekommen sind, ergänzen; entfernte streichen. Mermaid-Labels mit Sonderzeichen in Anführungszeichen setzen.

5. **Abschluss:** kurz auflisten, was sich gegenüber der vorigen Doku-Version geändert hat. Nicht committen, außer der Nutzer bittet darum.
