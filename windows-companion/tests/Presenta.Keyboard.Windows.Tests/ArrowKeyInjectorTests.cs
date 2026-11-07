using System.Runtime.InteropServices;
using Presenta.Domain.Keyboard;
using Presenta.Keyboard.Windows;

namespace Presenta.Keyboard.Windows.Tests;

/// <summary>
/// These tests never press a key. They check which key codes and input structures the Windows keyboard would send.
/// Real key injection is checked by hand (see docs/manual-test-guide.md).
/// </summary>
public class ArrowKeyInjectorTests
{
    [Fact]
    public void Next_maps_to_the_right_arrow_and_previous_to_the_left_arrow()
    {
        Assert.Equal(0x27, ArrowKeyInjector.VirtualKeyFor(SlideDirection.Next));
        Assert.Equal(0x25, ArrowKeyInjector.VirtualKeyFor(SlideDirection.Previous));
    }

    [Fact]
    public void Only_the_two_arrow_keys_can_ever_be_built()
    {
        var keys = Enum.GetValues<SlideDirection>()
            .SelectMany(ArrowKeyInjector.BuildKeyPress)
            .Select(input => input.Data.Keyboard.VirtualKey)
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal(new ushort[] { 0x25, 0x27 }, keys);
    }

    [Theory]
    [InlineData(SlideDirection.Next)]
    [InlineData(SlideDirection.Previous)]
    public void A_key_press_is_a_key_down_followed_by_a_key_up_of_the_same_extended_key(SlideDirection direction)
    {
        var events = ArrowKeyInjector.BuildKeyPress(direction);

        Assert.Equal(2, events.Length);
        Assert.All(events, e => Assert.Equal(1u, e.Type)); // keyboard input, never mouse or hardware
        Assert.Equal(0x0001u, events[0].Data.Keyboard.Flags);
        Assert.Equal(0x0003u, events[1].Data.Keyboard.Flags);
        Assert.Equal(events[0].Data.Keyboard.VirtualKey, events[1].Data.Keyboard.VirtualKey);
        Assert.All(events, e => Assert.Equal(0, (int)e.Data.Keyboard.ScanCode));
    }

    [Fact]
    public void Values_outside_the_enum_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArrowKeyInjector.BuildKeyPress((SlideDirection)7));
    }

    [Fact]
    public void The_input_structure_has_the_size_windows_expects()
    {
        var expected = Environment.Is64BitProcess ? 40 : 28;

        Assert.Equal(expected, Marshal.SizeOf<Input>());
    }

    [Fact]
    public void The_slide_direction_enum_offers_nothing_but_next_and_previous()
    {
        Assert.Equal(new[] { SlideDirection.Next, SlideDirection.Previous }, Enum.GetValues<SlideDirection>());
    }
}
