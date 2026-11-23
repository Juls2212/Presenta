using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Presenta.Targeting;

/// <summary>
/// Builds the plain-text diagnostic report (in English) that the user can send when keys do not reach the presentation.
/// It runs the same activation as a real send with a trace attached and presses no key. It never contains the pairing
/// code: it only knows windows, not the connection. Window titles can contain file names, which the report warns about.
/// </summary>
public static class TargetDiagnosticReport
{
    public static string Create(PresentationTargetService service, IWindowSystem windows, TimeProvider time)
    {
        var identity = service.Identity;
        var resolution = service.Resolve();
        var trace = new ActivationTrace();
        var result = service.Activate(trace);

        var text = new StringBuilder();
        text.AppendLine("Presenta window diagnostic report");
        text.AppendLine("NOTE: window titles can contain file names or other private text. Read this report and remove anything you do not want to share before you send it.");
        text.AppendLine("No key was sent while this report was made. It ran the same steps as a real send (restore, bring to front, confirm) and stopped before the key.");
        text.AppendLine();
        text.AppendLine("== System ==");
        text.AppendLine("Created (UTC): " + time.GetUtcNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        text.AppendLine("Windows: " + RuntimeInformation.OSDescription + ", version " + Environment.OSVersion.Version);
        text.AppendLine("Companion process: pid " + windows.CurrentProcessId + ", elevated: " + Yes(windows.IsCurrentProcessElevated));
        text.AppendLine();
        text.AppendLine("== Selected target ==");
        if (identity is null)
        {
            text.AppendLine("Nothing is selected.");
        }
        else
        {
            text.AppendLine($"Saved identity: process '{identity.ProcessName}', class '{identity.WindowClass}', title part '{identity.TitleMatch}'");
        }

        text.AppendLine("Resolution: " + resolution.State);
        if (trace.Target is { } target)
        {
            text.AppendLine("Target: " + Describe(target));
            AppendOwnerChain(text, windows, target);
            AppendSameProcessWindows(text, windows, target);
        }
        else if (resolution.Window is { } known)
        {
            text.AppendLine("Target: " + Describe(windows.GetDetails(known.Handle) ?? new WindowDetails(known, false, false, 0, 0)));
        }

        text.AppendLine();
        text.AppendLine("== Foreground before activation ==");
        text.AppendLine(trace.ForegroundBefore is { } before ? Describe(before) : "(none)");
        if (trace.ForegroundBefore is not null)
        {
            AppendOwnerChain(text, windows, trace.ForegroundBefore);
        }

        text.AppendLine("Relation to the target: " + Relation(trace.RelationBefore));
        text.AppendLine();
        text.AppendLine("== Steps ==");
        if (trace.RestoreAttempted)
        {
            text.AppendLine("Restore (the window was minimized): " + (trace.RestoreResult ? "succeeded" : "failed"));
        }
        else
        {
            text.AppendLine("Restore: not needed");
        }

        if (trace.Attempts.Count == 0)
        {
            text.AppendLine("No activation attempt was made (" + (trace.RelationBefore is ForegroundRelation.Target or ForegroundRelation.OwnedByTarget
                ? "an accepted window was already in front"
                : "the target could not be used") + ").");
        }

        var number = 0;
        foreach (var attempt in trace.Attempts)
        {
            number++;
            var step = attempt.Attempt;
            text.AppendLine($"Attempt {number}:");
            text.AppendLine($"  Foreground thread {step.ForegroundThread}, companion thread {step.CurrentThread}");
            text.AppendLine($"  AttachThreadInput(companion, foreground, attach): {Step(step.Attached, step.AttachError, step.ForegroundThread == 0 || step.ForegroundThread == step.CurrentThread ? "skipped" : null)}");
            text.AppendLine($"  SetForegroundWindow: {Step(step.SetForegroundSucceeded, step.SetForegroundError)}");
            text.AppendLine($"  BringWindowToTop: {Step(step.BringToTopSucceeded, step.BringToTopError)}");
            text.AppendLine($"  AttachThreadInput(detach): {Step(step.Detached, step.DetachError, step.Attached ? null : "skipped")}");
            text.AppendLine($"  Accepted window in front within the confirmation time: {Yes(attempt.Confirmed)}");
            text.AppendLine("  Foreground after the wait: " + (attempt.ForegroundAfter is { } after ? Describe(after) : "(none)"));
            text.AppendLine("  Relation to the target: " + Relation(attempt.Relation));
        }

        text.AppendLine();
        text.AppendLine("== Decision ==");
        text.AppendLine("Activation outcome: " + result.Outcome);
        text.AppendLine(result.IsInForeground
            ? "A key WOULD be sent: the foreground window is the target or a window it owns."
            : "NO key would be sent: " + Reason(result.Outcome));
        return text.ToString();
    }

    private static void AppendOwnerChain(StringBuilder text, IWindowSystem windows, WindowDetails details)
    {
        var current = details;
        for (var level = 1; level <= 8 && current.Owner != 0; level++)
        {
            var owner = windows.GetDetails(current.Owner);
            if (owner is null)
            {
                text.AppendLine($"  owner {level}: 0x{current.Owner:X} (no longer exists)");
                return;
            }

            text.AppendLine($"  owner {level}: class '{owner.Info.ClassName}', title '{owner.Info.Title}', pid {owner.Info.ProcessId}, process '{owner.Info.ProcessName}'");
            current = owner;
        }

        text.AppendLine($"  root owner: {(details.RootOwner == 0 ? "none" : "0x" + details.RootOwner.ToString("X", CultureInfo.InvariantCulture))}");
    }

    private static void AppendSameProcessWindows(StringBuilder text, IWindowSystem windows, WindowDetails target)
    {
        text.AppendLine($"All top-level windows of process {target.Info.ProcessId} ('{target.Info.ProcessName}'), to see how the program's windows relate:");
        foreach (var window in windows.EnumerateTopLevelWindows().Where(w => w.ProcessId == target.Info.ProcessId))
        {
            var details = windows.GetDetails(window.Handle) ?? new WindowDetails(window, false, false, 0, 0);
            text.AppendLine("  " + Describe(details) + (window.Handle == target.Info.Handle ? "  <- the selected target" : string.Empty));
        }
    }

    private static string Describe(WindowDetails details)
    {
        var info = details.Info;
        return $"handle 0x{info.Handle:X}, class '{info.ClassName}', title '{info.Title}', pid {info.ProcessId}, process '{info.ProcessName}', "
            + $"visible {Yes(info.IsVisible)}, minimized {Yes(info.IsMinimized)}, maximized {Yes(details.IsMaximized)}, cloaked {Yes(info.IsCloaked)}, "
            + $"elevated {Yes(info.IsElevated)}, top-most {Yes(details.IsTopMost)}, owner {(details.Owner == 0 ? "none" : "0x" + details.Owner.ToString("X", CultureInfo.InvariantCulture))}";
    }

    private static string Step(bool succeeded, int error, string? skipped = null) =>
        skipped is not null ? skipped : succeeded ? "succeeded" : $"failed, Win32 error {error}";

    private static string Yes(bool value) => value ? "yes" : "no";

    private static string Relation(ForegroundRelation relation) => relation switch
    {
        ForegroundRelation.Target => "it is the target",
        ForegroundRelation.OwnedByTarget => "it is a window owned by the target (accepted)",
        ForegroundRelation.OwnerOfTarget => "it is the owner of the target (not accepted)",
        ForegroundRelation.SameProcessOther => "another window of the same program, not owner or owned (not accepted)",
        ForegroundRelation.OtherProcess => "a window of another program (not accepted)",
        _ => "unknown or no window",
    };

    private static string Reason(ActivationOutcome outcome) => outcome switch
    {
        ActivationOutcome.NoTargetSelected => "no target is selected.",
        ActivationOutcome.TargetNotFound => "the selected window cannot be found.",
        ActivationOutcome.TargetAmbiguous => "several windows match the selected one.",
        ActivationOutcome.TargetElevated => "the target runs elevated and the companion does not.",
        ActivationOutcome.ActivationFailed => "Windows refused to bring the target to the front in every attempt.",
        ActivationOutcome.NotConfirmedForeground => "the target was never confirmed in front (see the relation after each attempt).",
        _ => "unknown.",
    };
}
