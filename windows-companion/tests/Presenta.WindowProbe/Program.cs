using System.Globalization;
using Presenta.Keyboard.Windows;

// Usage: Presenta.WindowProbe --title <text> --log <file> --ready <file>
//        [--owned-title <text> --owned-log <file> --owned-ready <file>] [--topmost] [--fullscreen]
// Opens a window, brings itself to the front, writes its handle to the ready file, and appends "Right" or "Left" to the log
// file for every arrow key it receives. It closes when the window is closed.
//
// --owned-title also opens a second window OWNED by the first one (the way a slide-show window can be owned by the main
// frame of its program). Whenever the first window is activated, the owned window takes the foreground, imitating a program
// that hands the focus to its show window. The owned window logs its own keys and writes its handle to --owned-ready.
// --topmost gives the first window the top-most style. --fullscreen makes it borderless and maximized.
static string? Arg(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

// --hold-event <name> --ready <file>: no window. Creates (or opens) a named event, writes "created" or "existing" to the ready
// file, then waits for a line on standard input (or for the process to be killed) before exiting. For the single-instance test.
var holdEvent = Arg(args, "--hold-event");
if (holdEvent is not null)
{
    var readyFile = Arg(args, "--ready");
    if (readyFile is null)
    {
        return 2;
    }

    using var held = new EventWaitHandle(false, EventResetMode.ManualReset, holdEvent, out var createdNew);
    File.WriteAllText(readyFile, createdNew ? "created" : "existing");
    Console.In.ReadLine();
    return 0;
}

var title = Arg(args, "--title");
var log = Arg(args, "--log");
var ready = Arg(args, "--ready");
if (title is null || log is null || ready is null)
{
    return 2;
}

var ownedTitle = Arg(args, "--owned-title");
var ownedLog = Arg(args, "--owned-log");
var ownedReady = Arg(args, "--owned-ready");
var options = new ProbeOptions(
    ownedTitle is not null && ownedLog is not null && ownedReady is not null ? new OwnedOptions(ownedTitle, ownedLog, ownedReady) : null,
    Array.IndexOf(args, "--topmost") >= 0,
    Array.IndexOf(args, "--fullscreen") >= 0);

Application.EnableVisualStyles();
Application.Run(new ProbeForm(title, log, ready, options, owner: null));
return 0;

internal sealed record OwnedOptions(string Title, string Log, string Ready);

internal sealed record ProbeOptions(OwnedOptions? Owned, bool TopMost, bool FullScreen);

internal sealed class ProbeForm : Form
{
    private readonly string _log;
    private readonly string _ready;
    private readonly ProbeOptions _options;
    private ProbeForm? _owned;

    public ProbeForm(string title, string log, string ready, ProbeOptions options, ProbeForm? owner)
    {
        _log = log;
        _ready = ready;
        _options = options;
        Text = title;
        Width = 420;
        Height = 160;
        StartPosition = FormStartPosition.CenterScreen;
        if (owner is null)
        {
            TopMost = options.TopMost;
            if (options.FullScreen)
            {
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
        }

        Shown += (_, _) =>
        {
            if (owner is null && options.Owned is { } owned)
            {
                _owned = new ProbeForm(owned.Title, owned.Log, owned.Ready, new ProbeOptions(null, false, false), this);
                _owned.Show(this);
            }

            new Win32WindowSystem().TryBringToForeground(Handle);
            File.WriteAllText(_ready, Handle.ToInt64().ToString(CultureInfo.InvariantCulture));
        };

    }

    // The owner hands the focus to its owned window as part of its own activation, before any other program can look at it.
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        const int WmActivate = 0x0006;
        if (m.Msg == WmActivate && (m.WParam.ToInt64() & 0xFFFF) != 0 && _owned is { IsDisposed: false, IsHandleCreated: true } window && !_handing)
        {
            _handing = true;
            try
            {
                new Win32WindowSystem().TryBringToForeground(window.Handle);
            }
            finally
            {
                _handing = false;
            }
        }
    }

    private bool _handing;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Right or Keys.Left)
        {
            File.AppendAllText(_log, keyData + Environment.NewLine);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}
