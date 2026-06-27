using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using BlitzBrief.Core;

namespace BlitzBrief.Windows.Platform;

/// <summary>
/// Liest den Text links und rechts vom Cursor in der Vordergrund-App. Strategie-Kette:
/// 1. Office-COM für Word/Outlook-Desktop (präzise),
/// 2. UI Automation / TextPattern (universell, zerstörungsfrei) für alles andere.
/// Kein Clipboard-Trick (bewusst zerstörungsfrei). UIA/COM laufen auf einem MTA-Thread
/// mit hartem Timeout, damit der Aufnahme-Start nie blockiert.
/// </summary>
public sealed class CursorContextReader(Action<string>? log = null) : ICursorContextReader
{
    // Wie viel Text je Seite wir maximal anfordern; Core kürzt links auf den Satz und prüft
    // rechts nur die unmittelbare Umgebung.
    private const int PrecedingChars = 400;
    private const int FollowingChars = 160;
    private const int ReadTimeoutMs = 600;

    public CursorSurroundings Read() =>
        RunOnMtaWithTimeout(ReadSurroundings, ReadTimeoutMs) ?? CursorSurroundings.Empty;

    private CursorSurroundings? ReadSurroundings()
    {
        try
        {
            var process = ForegroundProcessName();
            if (string.Equals(process, "WINWORD", StringComparison.OrdinalIgnoreCase))
            {
                var viaCom = TryReadOffice("Word.Application", ReadWord);
                if (viaCom is not null)
                {
                    return viaCom;
                }
            }
            else if (string.Equals(process, "OUTLOOK", StringComparison.OrdinalIgnoreCase))
            {
                var viaCom = TryReadOffice("Outlook.Application", ReadOutlook);
                if (viaCom is not null)
                {
                    return viaCom;
                }
            }

            return ReadViaUia();
        }
        catch (Exception ex)
        {
            log?.Invoke($"CursorContextReader.ReadSurroundings failed: {ex.Message}");
            return null;
        }
    }

    // ── UI Automation (universell, zerstörungsfrei) ──────────────────────────

    private CursorSurroundings? ReadViaUia()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null || !focused.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj))
            {
                return null;
            }

            var textPattern = (TextPattern)patternObj;
            var selection = textPattern.GetSelection();
            if (selection is null || selection.Length == 0)
            {
                return null;
            }

            var caret = selection[0];

            // Links: vom Selektionsanfang (= Einfügemarke) um PrecedingChars nach links.
            var left = caret.Clone();
            left.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
            left.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -PrecedingChars);

            // Rechts: vom Selektionsende um FollowingChars nach rechts.
            var right = caret.Clone();
            right.MoveEndpointByRange(TextPatternRangeEndpoint.Start, caret, TextPatternRangeEndpoint.End);
            right.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, FollowingChars);

            return new CursorSurroundings(Nullable(left.GetText(-1)), Nullable(right.GetText(-1)));
        }
        catch (Exception ex)
        {
            log?.Invoke($"CursorContextReader UIA failed: {ex.Message}");
            return null;
        }
    }

    // ── Office-COM (präzise für Word/Outlook-Desktop) ────────────────────────

    private CursorSurroundings? TryReadOffice(string progId, Func<dynamic, CursorSurroundings?> read)
    {
        object? app = null;
        try
        {
            app = GetActiveComObject(progId);
            return app is null ? null : read(app);
        }
        catch (Exception ex)
        {
            log?.Invoke($"CursorContextReader COM ({progId}) failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (app is not null)
            {
                try { Marshal.ReleaseComObject(app); } catch { }
            }
        }
    }

    private static CursorSurroundings? ReadWord(dynamic app) => ReadWordDocument(app.Selection.Document, app.Selection);

    private static CursorSurroundings? ReadOutlook(dynamic app)
    {
        dynamic inspector = app.ActiveInspector();
        if (inspector is null)
        {
            return null;
        }

        // Outlook-Mailtext ist ein Word-Dokument (WordEditor) – dieselbe Range-Logik wie Word.
        dynamic document = inspector.WordEditor;
        return document is null ? null : ReadWordDocument(document, document.Application.Selection);
    }

    private static CursorSurroundings? ReadWordDocument(dynamic document, dynamic selection)
    {
        int selStart = selection.Start;
        int selEnd = selection.End;
        int docEnd = document.Content.End;

        int from = Math.Max(0, selStart - PrecedingChars);
        int to = Math.Min(selEnd + FollowingChars, docEnd);

        string? preceding = selStart > from ? document.Range(from, selStart).Text as string : null;
        string? following = to > selEnd ? document.Range(selEnd, to).Text as string : null;
        return new CursorSurroundings(Nullable(preceding), Nullable(following));
    }

    // ── Helfer ───────────────────────────────────────────────────────────────

    private static string? Nullable(string? text) => string.IsNullOrEmpty(text) ? null : text;

    // UIA-Clientaufrufe gehören in einen MTA-Apartment; der WPF-UI-Thread ist STA und kann
    // dort hängen/langsam sein. Eigener MTA-Thread mit Timeout = nie blockierender Start.
    private CursorSurroundings? RunOnMtaWithTimeout(Func<CursorSurroundings?> work, int timeoutMs)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA)
        {
            return work();
        }

        CursorSurroundings? result = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { log?.Invoke($"CursorContextReader MTA worker failed: {ex.Message}"); }
        })
        {
            IsBackground = true,
            Name = "CursorContextReader",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        if (!thread.Join(timeoutMs))
        {
            log?.Invoke("CursorContextReader read timed out.");
            return null;
        }

        return result;
    }

    private static string ForegroundProcessName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return "";
        }

        _ = GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    // Marshal.GetActiveObject existiert in .NET (Core/5+) NICHT mehr → selbst via ole32/oleaut32.
    private static object? GetActiveComObject(string progId)
    {
        try
        {
            CLSIDFromProgID(progId, out var clsid);
            GetActiveObject(ref clsid, IntPtr.Zero, out var obj);
            return obj;
        }
        catch
        {
            // Kein laufendes Office mit dieser ProgID greifbar (ROT leer / Rechte) → UIA-Fallback.
            return null;
        }
    }

    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
