namespace Presenta.Domain.Keyboard;

/// <summary>
/// The only two things the companion can ask the keyboard to do. There is deliberately no way to request any other key:
/// the Windows implementation maps <see cref="Next"/> to the Right arrow and <see cref="Previous"/> to the Left arrow.
/// </summary>
public enum SlideDirection
{
    Next,
    Previous,
}

/// <summary>Outcome of one request to press a slide key. Anything other than <see cref="Delivered"/> means no key was pressed.</summary>
public enum KeyDeliveryResult
{
    /// <summary>The target window was confirmed to be in the foreground and the key was pressed.</summary>
    Delivered,

    /// <summary>No presentation window has been chosen.</summary>
    NoTargetSelected,

    /// <summary>The chosen window cannot be found any more (closed, or its title changed beyond recognition).</summary>
    TargetNotFound,

    /// <summary>More than one open window matches the chosen one, so the right one cannot be told apart.</summary>
    TargetAmbiguous,

    /// <summary>The chosen window belongs to an elevated process and the companion is not elevated: Windows blocks input into it.</summary>
    TargetElevated,

    /// <summary>Windows refused to bring the target to the foreground.</summary>
    ActivationFailed,

    /// <summary>The activation call returned but the target never became the foreground window within the time limit.</summary>
    NotConfirmedForeground,

    /// <summary>The target was in the foreground but the operating system did not accept the key events, or the keyboard failed unexpectedly.</summary>
    SendFailed,
}

/// <summary>State of the chosen presentation window as the companion sees it right now.</summary>
public enum TargetState
{
    /// <summary>A single matching window exists and can receive keys.</summary>
    Ready,

    /// <summary>Nothing has been chosen.</summary>
    NoneSelected,

    /// <summary>The chosen window cannot be found.</summary>
    NotFound,

    /// <summary>Several windows match the chosen one.</summary>
    Ambiguous,

    /// <summary>The window exists but is minimized. It is restored before a key is sent.</summary>
    Minimized,

    /// <summary>The window belongs to an elevated process the companion cannot send input to.</summary>
    Elevated,
}

/// <summary>
/// The only route from the companion to the keyboard. Implementations must send nothing but the Right arrow (Next)
/// or the Left arrow (Previous), and must send nothing at all unless the intended presentation window is confirmed
/// to be the foreground window.
/// </summary>
public interface ISlideKeyboard
{
    /// <summary>
    /// Brings the presentation window to the front, confirms it is there, then presses and releases the arrow key for
    /// <paramref name="direction"/>. Expected failures are returned, not thrown; no key is pressed on failure.
    /// </summary>
    KeyDeliveryResult Send(SlideDirection direction);

    /// <summary>Resolves the chosen presentation window without activating it and without pressing any key.</summary>
    TargetState GetTargetState();
}
