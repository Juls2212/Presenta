using System.Globalization;
using Presenta.Keyboard.Windows;

// Usage: Presenta.WindowProbe --title <text> --log <file> --ready <file>
// Opens a window, brings itself to the front, writes its handle to the ready file, and appends "Right" or "Left" to the log
// file for every arrow key it receives. It closes when the window is closed.
static string? Arg(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

var title = Arg(args, "--title");
var log = Arg(args, "--log");
var ready = Arg(args, "--ready");
if (title is null || log is null || ready is null)
{
    return 2;
}

Application.EnableVisualStyles();
Application.Run(new ProbeForm(title, log, ready));
return 0;

internal sealed class ProbeForm : Form
{
    private readonly string _log;
    private readonly string _ready;

    public ProbeForm(string title, string log, string ready)
    {
        _log = log;
        _ready = ready;
        Text = title;
        Width = 420;
        Height = 160;
        StartPosition = FormStartPosition.CenterScreen;
        Shown += (_, _) =>
        {
            new Win32WindowSystem().TryBringToForeground(Handle);
            File.WriteAllText(_ready, Handle.ToInt64().ToString(CultureInfo.InvariantCulture));
        };
    }

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
