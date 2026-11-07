using System.Runtime.InteropServices;
using Presenta.Domain.Keyboard;

namespace Presenta.Keyboard.Windows;

[StructLayout(LayoutKind.Sequential)]
internal struct MouseInput
{
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KeyboardInput
{
    public ushort VirtualKey;
    public ushort ScanCode;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

/// <summary>The INPUT union. The mouse member only exists so the structure has the size Windows expects.</summary>
[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)]
    public MouseInput Mouse;

    [FieldOffset(0)]
    public KeyboardInput Keyboard;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Input
{
    public uint Type;
    public InputUnion Data;
}

internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint inputCount, ref Input inputs, int inputSize);
}

/// <summary>
/// Presses the Right arrow (next slide) or the Left arrow (previous slide) with the Windows SendInput API.
/// No other key can be requested: <see cref="SlideDirection"/> has two values and every other value is rejected.
/// <para>
/// SendInput delivers to whichever window is in the foreground, so this class is internal: the only caller is
/// <see cref="Win32WindowSystem.SendArrow"/>, which <c>WindowSlideKeyboard</c> uses only after confirming that the
/// presentation window is the foreground window.
/// </para>
/// </summary>
internal static class ArrowKeyInjector
{
    internal const ushort VirtualKeyLeft = 0x25;
    internal const ushort VirtualKeyRight = 0x27;
    internal const uint InputKeyboard = 1;
    internal const uint KeyEventExtendedKey = 0x0001;
    internal const uint KeyEventKeyUp = 0x0002;

    /// <summary>Presses and releases the key. Returns false when Windows did not accept every event.</summary>
    public static bool Press(SlideDirection direction)
    {
        foreach (var input in BuildKeyPress(direction))
        {
            var copy = input;
            if (NativeMethods.SendInput(1, ref copy, Marshal.SizeOf<Input>()) != 1)
            {
                return false;
            }
        }

        return true;
    }

    internal static ushort VirtualKeyFor(SlideDirection direction) => direction switch
    {
        SlideDirection.Next => VirtualKeyRight,
        SlideDirection.Previous => VirtualKeyLeft,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Only Next and Previous are supported."),
    };

    /// <summary>The key-down and key-up events for <paramref name="direction"/> (arrow keys are "extended" keys).</summary>
    internal static Input[] BuildKeyPress(SlideDirection direction)
    {
        var key = VirtualKeyFor(direction);
        return
        [
            KeyEvent(key, KeyEventExtendedKey),
            KeyEvent(key, KeyEventExtendedKey | KeyEventKeyUp),
        ];
    }

    private static Input KeyEvent(ushort virtualKey, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = flags } },
    };
}
