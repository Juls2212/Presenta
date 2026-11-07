using Presenta.Targeting;

namespace Presenta.Targeting.Tests;

public sealed class TargetSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "presenta-target-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "target.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void A_missing_file_means_none_selected()
    {
        Assert.Null(new JsonTargetSettingsStore(FilePath).Load());
    }

    [Fact]
    public void The_identity_survives_a_save_and_load_round_trip_and_the_folder_is_created()
    {
        var store = new JsonTargetSettingsStore(FilePath);
        var identity = new PresentationTargetIdentity("POWERPNT", "screenClass", "Slide Show - deck.pptx");

        Assert.True(store.Save(identity));

        Assert.Equal(identity, new JsonTargetSettingsStore(FilePath).Load());
    }

    [Fact]
    public void Saving_again_replaces_the_previous_choice_and_leaves_no_temporary_file()
    {
        var store = new JsonTargetSettingsStore(FilePath);
        store.Save(new PresentationTargetIdentity("a", "b", "c"));
        store.Save(new PresentationTargetIdentity("notepad", "Notepad", "notes"));

        Assert.Equal("notepad", store.Load()!.ProcessName);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Clearing_saves_none_selected()
    {
        var store = new JsonTargetSettingsStore(FilePath);
        store.Save(new PresentationTargetIdentity("a", "b", "c"));

        store.Save(null);

        Assert.Null(store.Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{ \"version\": 1, \"target\": ")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{ \"target\": 5 }")]
    [InlineData("{ \"target\": { \"processName\": \"x\" } }")]
    [InlineData("{ \"target\": { \"processName\": 1, \"windowClass\": 2 } }")]
    public void A_corrupt_or_incomplete_file_falls_back_to_none_selected_without_crashing(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, content);

        Assert.Null(new JsonTargetSettingsStore(FilePath).Load());
    }

    [Fact]
    public void A_file_with_invalid_utf8_falls_back_to_none_selected()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(FilePath, [0xFF, 0xFE, 0xFD, 0x7B]);

        Assert.Null(new JsonTargetSettingsStore(FilePath).Load());
    }

    [Fact]
    public void An_unwritable_location_reports_failure_instead_of_throwing()
    {
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "a file where a folder is needed");

        Assert.False(new JsonTargetSettingsStore(Path.Combine(blocker, "target.json")).Save(new PresentationTargetIdentity("a", "b", "c")));
    }

    [Fact]
    public void The_file_holds_only_the_three_identity_fields_and_a_version()
    {
        new JsonTargetSettingsStore(FilePath).Save(new PresentationTargetIdentity("POWERPNT", "screenClass", "deck"));

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(FilePath));
        var root = document.RootElement;
        var names = root.EnumerateObject().Select(p => p.Name).Order().ToArray();
        var target = root.GetProperty("target").EnumerateObject().Select(p => p.Name).Order().ToArray();

        Assert.Equal(new[] { "target", "version" }, names);
        Assert.Equal(new[] { "processName", "titleMatch", "windowClass" }, target);
    }

    [Fact]
    public void The_default_path_is_under_the_local_application_data_folder()
    {
        var path = JsonTargetSettingsStore.DefaultPath();

        Assert.EndsWith(Path.Combine("Presenta", "target.json"), path, StringComparison.Ordinal);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), path, StringComparison.Ordinal);
    }
}

public class IdentityTests
{
    [Fact]
    public void An_empty_title_part_matches_any_title_of_the_same_program_and_class()
    {
        var identity = new PresentationTargetIdentity("app", "Cls", string.Empty);

        Assert.True(identity.Matches(new WindowInfo(1, "anything", "Cls", 5, "APP")));
        Assert.False(identity.Matches(new WindowInfo(1, "anything", "cls", 5, "APP"))); // class names are case-sensitive
        Assert.False(identity.Matches(new WindowInfo(1, "anything", "Cls", 5, "other")));
    }

    [Theory]
    [InlineData("*Untitled - Notepad", "Untitled - Notepad")]
    [InlineData("  Untitled - Notepad  ", "Untitled - Notepad")]
    [InlineData("● Draft", "Draft")]
    public void The_stored_title_ignores_modified_markers_and_spaces(string title, string expected)
    {
        Assert.Equal(expected, PresentationTargetIdentity.From(new WindowInfo(1, title, "C", 1, "p")).TitleMatch);
    }
}
