using MusicWorker.Core.Persistence;

namespace MusicWorker.Core.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mw-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void A_crashed_session_with_saved_work_is_found()
    {
        var store = new RecoveryStore(_root);
        var crashed = store.StartSession(new SessionInfo(111, 1));
        Directory.CreateDirectory(crashed.EditorFolder);
        new EditorRecovery { DisplayName = "Lied.wav", AudioFile = "audio.wav", OriginalPath = @"C:\Musik\Lied.wav" }.Write(crashed.EditorFolder);
        var running = store.StartSession(new SessionInfo(222, 2));
        Directory.CreateDirectory(running.EditorFolder);
        new EditorRecovery().Write(running.EditorFolder);

        var abandoned = store.FindAbandoned(s => s.ProcessId == 222);

        var session = Assert.Single(abandoned);
        Assert.Equal(crashed.Folder, session.Folder);
        Assert.True(session.HasEditor);
        Assert.False(session.HasMixer);
        Assert.Equal(@"C:\Musik\Lied.wav", EditorRecovery.Read(Path.Combine(session.Folder, RecoveryStore.EditorFolderName))?.OriginalPath);
    }

    [Fact]
    public void Empty_abandoned_sessions_are_cleaned_up_and_ended_sessions_vanish()
    {
        var store = new RecoveryStore(_root);
        var empty = store.StartSession(new SessionInfo(1, 1));
        var ended = store.StartSession(new SessionInfo(2, 2));
        Directory.CreateDirectory(ended.MixerFolder);
        File.WriteAllText(Path.Combine(ended.MixerFolder, "Projekt.mwproj"), "{}");

        ended.End();
        var abandoned = store.FindAbandoned(_ => false);

        Assert.Empty(abandoned);
        Assert.False(Directory.Exists(empty.Folder));
        Assert.False(Directory.Exists(ended.Folder));
    }

    [Fact]
    public void Mixer_recovery_is_detected()
    {
        var store = new RecoveryStore(_root);
        var session = store.StartSession(new SessionInfo(5, 5));
        Directory.CreateDirectory(session.MixerFolder);
        File.WriteAllText(Path.Combine(session.MixerFolder, "Projekt.mwproj"), "{}");

        var found = Assert.Single(store.FindAbandoned(_ => false));

        Assert.True(found.HasMixer);
        Assert.False(found.HasEditor);
    }

    [Fact]
    public void Atomic_write_replaces_existing_files()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "a.json");
        File.WriteAllText(path, "alt");

        AtomicFile.WriteAllText(path, "neu");

        Assert.Equal("neu", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Presets_are_saved_replaced_and_deleted()
    {
        string path = Path.Combine(_root, "presets.json");
        var store = new PresetStore(path);
        int changes = 0;
        store.Changed += (_, _) => changes++;

        store.Save(new EffectPreset("Kompressor", " Meine Stimme ", new Dictionary<string, double> { ["Ratio"] = 3 }));
        store.Save(new EffectPreset("Kompressor", "meine stimme", new Dictionary<string, double> { ["Ratio"] = 4 }));
        store.Save(new EffectPreset("Klang", "Podcast", new Dictionary<string, double> { ["Gate.On"] = 1 }));

        var reloaded = new PresetStore(path);
        var compressor = Assert.Single(reloaded.For("Kompressor"));
        Assert.Equal(4, compressor.Values["Ratio"]);
        Assert.Single(reloaded.For("Klang"));
        Assert.True(reloaded.Delete("Klang", "PODCAST"));
        Assert.Empty(new PresetStore(path).For("Klang"));
        Assert.Equal(3, changes);
        Assert.Throws<ArgumentException>(() => store.Save(new EffectPreset("Hall", "  ", new Dictionary<string, double>())));
    }

    [Fact]
    public void Broken_preset_files_are_ignored()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "presets.json");
        File.WriteAllText(path, "{ kaputt");

        Assert.Empty(new PresetStore(path).For("Hall"));
    }

    [Theory]
    [InlineData("v0.8.0", "0.8.0")]
    [InlineData("0.8", "0.8.0")]
    [InlineData("V1.0.0-beta", "1.0.0")]
    [InlineData("v0.10.2", "0.10.2")]
    public void Versions_are_parsed(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateInfo.ParseVersion(tag));
    }

    [Fact]
    public void Github_release_is_read_and_compared()
    {
        const string json = """
            {"tag_name":"v0.10.0","html_url":"https://github.com/x/y/releases/tag/v0.10.0","draft":false,
             "assets":[{"name":"MusicWorker-v0.10.0-win-x64.zip","browser_download_url":"https://a/zip"},
                       {"name":"MusicWorker-Setup-0.10.0.exe","browser_download_url":"https://a/setup.exe"}]}
            """;

        var info = UpdateInfo.Parse(json);

        Assert.NotNull(info);
        Assert.Equal("https://a/setup.exe", info!.SetupUrl);
        Assert.True(info.IsNewerThan(new Version(0, 9, 5)));
        Assert.True(info.IsNewerThan(new Version(0, 8, 0, 0)));
        Assert.False(info.IsNewerThan(new Version(0, 10, 0, 0)));
        Assert.Null(UpdateInfo.Parse("{\"message\":\"Not Found\"}"));
        Assert.Null(UpdateInfo.Parse("kein json"));
    }
}
