using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using BlitzBrief.Core;

namespace BlitzBrief.Windows.Platform;

/// <summary>
/// Liest den Satz links vom Cursor aus der Vordergrund-App. Strategie-Kette:
/// 1. Office-COM für Word/Outlook-Desktop (präzise),
/// 2. UI Automation / TextPattern (universell, zerstörungsfrei) für alles andere.
/// Kein Clipboard-Trick (bewusst zerstörungsfrei). UIA/COM laufen auf einem MTA-Thread
/// mit hartem Timeout, damit der Aufnahme-Start nie blockiert.
/// </summary>
public sealed class CursorContextReader(Action<string>? log = null) : ICursorContextReader
{
    // Wie viel Text links vom Cursor wir maximal anfordern; SentenceContext kürzt auf den Satz.
    private const int PrecedingChars = 400;
    private const int ReadTimeoutMs = 600;

    public string? ReadCurrentSentence()
    {
        var preceding = RunOnMtaWithTimeout(ReadPrecedingText, ReadTimeoutMs);
        return SentenceContext.CurrentSentence(preceding);
    }

    private string? ReadPrecedingText()
    {
        try
        {
            var process = ForegroundProcessName();
            if (string.Equals(process, "WINWORD", StringComparison.OrdinalIgnoreCase))
            {
                var viaCom = TryReadOffice("Word.Application", ReadWordPreceding);
                if (!string.IsNullOrEmpty(viaCom))
                {
                    return viaCom;
                }
            }
            else if (string.Equals(process, "OUTLOOK", StringComparison.OrdinalIgnoreCase))
            {
                var viaCom = TryReadOffice("Outlook.Application", ReadOutlookPreceding);
                if (!string.IsNullOrEmpty(viaCom))
                {
                    return viaCom;
                }
            }

            return ReadViaUia();
        }
        catch (Exception ex)
        {
            log?.Invoke($"CursorContextReader.ReadPrecedingText failed: {ex.Message}");
            return null;
        }
    }

    // ── UI Automation (universell, zerstörungsfrei) ──────────────────────────

    private string? ReadViaUia()
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

            // Range vom Selektionsanfang (= Einfügemarke) um PrecedingChars nach links spannen.
            // So lesen wir nur Text VOR dem Cursor, auch wenn rechts etwas selektiert ist.
            var range = selection[0].Clone();
            range.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection[0], TextPatternRangeEndpoint.Start);
            range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -PrecedingChars);
            var text = range.GetText(-1);
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception ex)
        {
            log?.Invoke($"CursorContextReader UIA failed: {ex.Message}");
            return null;
        }
    }

    // ── Office-COM (präzise für Word/Outlook-Desktop) ────────────────────────

    private string? TryReadOffice(string progId, Func<dynamic, string?> read)
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

    private static string? ReadWordPreceding(dynamic app)
    {
        dynamic selection = app.Selection;
        int caret = selection.Start;
        if (caret <= 0)
        {
            return null;
        }

        int from = Math.Max(0, caret - PrecedingChars);
        dynamic document = selection.Document;
        dynamic range = document.Range(from, caret);
        return range.Text as string;
    }

    private static string? ReadOutlookPreceding(dynamic app)
    {
        dynamic inspector = app.ActiveInspector();
        if (inspector is null)
        {
            return null;
        }

        // Outlook-Mailtext ist ein Word-Dokument (WordEditor) – dieselbe Range-Logik wie Word.
        dynamic document = inspector.WordEditor;
        if (document is null)
        {
            return null;
        }

        dynamic selection = document.Application.Selection;
        int caret = selection.Start;
        if (caret <= 0)
        {
            return null;
        }

        int from = Math.Max(0, caret - PrecedingChars);
        dynamic range = document.Range(from, caret);
        return range.Text as string;
    }

    // ── Helfer ───────────────────────────────────────────────────────────────

    // UIA-Clientaufrufe gehören in einen MTA-Apartment; der WPF-UI-Thread ist STA und kann
    // dort hängen/langsam sein. Eigener MTA-Thread mit Timeout = nie blockierender Start.
    private string? RunOnMtaWithTimeout(Func<string?> work, int timeoutMs)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA)
        {
            return work();
        }

        string? result = null;
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
