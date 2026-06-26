namespace BlitzBrief.Core.Workflow;

/// <summary>
/// Signalisiert, dass keine verwertbare Aufnahme vorliegt (zu kurz, leer oder
/// nur ein zurückgespiegeltes Prompt-Echo). Das ist kein echter Fehler – der
/// Aufrufer behandelt diesen Fall still (kein Fehler-Popup).
/// </summary>
public sealed class EmptyRecordingException(string message) : Exception(message);
