using Presenta.Companion.ViewModels;

namespace Presenta.Companion.ViewModels.Tests;

public class LaunchArgumentsTests
{
    [Fact]
    public void No_arguments_means_defaults()
    {
        var parsed = LaunchArguments.Parse(Array.Empty<string>());

        Assert.Null(parsed.Port);
        Assert.Null(parsed.DatabasePath);
    }

    [Fact]
    public void Port_and_database_are_read()
    {
        var parsed = LaunchArguments.Parse(new[] { "--port", "9000", "--database", @"C:\temp\h.db" });

        Assert.Equal(9000, parsed.Port);
        Assert.Equal(@"C:\temp\h.db", parsed.DatabasePath);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("70000")]
    [InlineData("")]
    public void An_invalid_port_is_ignored(string value)
    {
        Assert.Null(LaunchArguments.Parse(new[] { "--port", value }).Port);
    }

    [Fact]
    public void Unknown_arguments_and_a_missing_value_are_ignored()
    {
        var parsed = LaunchArguments.Parse(new[] { "--verbose", "--port" });

        Assert.Null(parsed.Port);
    }

    [Fact]
    public void The_target_settings_path_is_read_and_defaults_to_none()
    {
        Assert.Null(LaunchArguments.Parse(Array.Empty<string>()).TargetSettingsPath);
        Assert.Equal(@"C:	emp	.json", LaunchArguments.Parse(new[] { "--target-settings", @"C:	emp	.json" }).TargetSettingsPath);
    }
}
