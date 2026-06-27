namespace BlitzBrief.Core;

/// <summary>
/// Liest den bereits geschriebenen Text links vom Cursor in der Vordergrund-Anwendung,
/// reduziert auf den aktuell angefangenen Satz (für die Fortsetzung im Kontext-Modus).
/// Liefert <c>null</c>, wenn kein Kontext verfügbar ist: App ohne Textzugriff, Cursor am
/// Satzanfang/-ende, Lese- oder Zeitüberschreitungsfehler. Plattformneutral; die
/// Windows-Implementierung sitzt in BlitzBrief.Windows (UI Automation + Office-COM).
/// </summary>
public interface ICursorContextReader
{
    string? ReadCurrentSentence();
}
