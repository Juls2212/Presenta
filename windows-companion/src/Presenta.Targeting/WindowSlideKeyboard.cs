using Presenta.Domain.Keyboard;

namespace Presenta.Targeting;

/// <summary>
/// <see cref="ISlideKeyboard"/> that aims at the chosen presentation window instead of whatever happens to be in the
/// foreground. Per key: resolve the target, restore it if minimized, bring it to the front, confirm it is the foreground
/// window, check once more immediately before pressing, and only then press the arrow key. If any step fails, nothing
/// is pressed and a typed result says why. Only the Right and Left arrows can ever be pressed (see <see cref="IWindowSystem.SendArrow"/>).
/// </summary>
public sealed class WindowSlideKeyboard : ISlideKeyboard
{
    private readonly PresentationTargetService _target;
    private readonly IWindowSystem _windows;

    public WindowSlideKeyboard(PresentationTargetService target, IWindowSystem windows)
    {
        _target = target;
        _windows = windows;
    }

    public KeyDeliveryResult Send(SlideDirection direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Only Next and Previous are supported.");
        }

        var activation = _target.Activate();
        switch (activation.Outcome)
        {
            case ActivationOutcome.InForeground:
                break;
            case ActivationOutcome.NoTargetSelected:
                return KeyDeliveryResult.NoTargetSelected;
            case ActivationOutcome.TargetNotFound:
                return KeyDeliveryResult.TargetNotFound;
            case ActivationOutcome.TargetAmbiguous:
                return KeyDeliveryResult.TargetAmbiguous;
            case ActivationOutcome.TargetElevated:
                return KeyDeliveryResult.TargetElevated;
            case ActivationOutcome.ActivationFailed:
                return KeyDeliveryResult.ActivationFailed;
            default:
                return KeyDeliveryResult.NotConfirmedForeground;
        }

        // The last look before the key: the user may have clicked elsewhere since the confirmation.
        if (_windows.GetForegroundWindow() != activation.Window!.Handle)
        {
            return KeyDeliveryResult.NotConfirmedForeground;
        }

        return _windows.SendArrow(direction) ? KeyDeliveryResult.Delivered : KeyDeliveryResult.SendFailed;
    }

    public TargetState GetTargetState() => _target.GetState();
}
