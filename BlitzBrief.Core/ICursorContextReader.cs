namespace BlitzBrief.Core;

/// <summary>
/// Roher Text unmittelbar links und rechts vom Cursor in der Vordergrund-App (untrimmt, wie
/// gelesen). Felder sind <c>null</c>, wenn an der Seite kein Text vorliegt oder das Lesen
/// fehlschlug. Daraus leiten <see cref="SentenceContext"/> und <see cref="SmartInsert"/> den
/// Satzkontext (für den whisper-Prompt) bzw. das passende Einfügen ab.
/// </summary>
public sealed record CursorSurroundings(string? Preceding, string? Following)
{
    public static readonly CursorSurroundings Empty = new(null, null);
}

/// <summary>
/// Liest den Text rund um den Cursor. Plattformneutral; die Windows-Implementierung sitzt in
/// BlitzBrief.Windows (UI Automation + Office-COM), zerstörungsfrei und ohne Clipboard.
/// </summary>
public interface ICursorContextReader
{
    CursorSurroundings Read();
}
