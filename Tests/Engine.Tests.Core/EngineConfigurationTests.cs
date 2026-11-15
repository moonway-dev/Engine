using Engine.Core;
using Xunit;

namespace Engine.Tests.Core;

public class EngineConfigurationTests
{
    private sealed class DisplaySection
    {
        public string Title { get; set; } = "Engine";
        public int Width { get; set; } = 1280;
        public int Height { get; set; } = 720;
        public bool VSync { get; set; } = true;
        public List<string> Tags { get; set; } = [];
    }

    [Fact]
    public void GetSection_ReturnsDefaultsWhenMissing()
    {
        EngineConfiguration configuration = new();
        DisplaySection section = configuration.GetSection<DisplaySection>("Display");
        Assert.Equal("Engine", section.Title);
        Assert.Equal(1280, section.Width);
    }

    [Fact]
    public void SetSection_RoundTripsThroughGetSection()
    {
        EngineConfiguration configuration = new();
        configuration.SetSection("Display", new DisplaySection { Title = "Custom", Width = 1920, Height = 1080, VSync = false, Tags = ["a", "b"] });
        DisplaySection loaded = configuration.GetSection<DisplaySection>("Display");
        Assert.Equal("Custom", loaded.Title);
        Assert.Equal(1920, loaded.Width);
        Assert.Equal(1080, loaded.Height);
        Assert.False(loaded.VSync);
        Assert.Equal(["a", "b"], loaded.Tags);
    }

    [Fact]
    public void SaveAndLoad_PreservesSections()
    {
        string path = Path.Combine(Path.GetTempPath(), "EngineTests", Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            EngineConfiguration original = new();
            original.SetSection("Display", new DisplaySection { Title = "Saved", Width = 2560, Tags = ["x"] });
            original.Save(path);

            EngineConfiguration loaded = new();
            Assert.True(loaded.TryLoad(path));
            DisplaySection section = loaded.GetSection<DisplaySection>("Display");
            Assert.Equal("Saved", section.Title);
            Assert.Equal(2560, section.Width);
            Assert.Equal(["x"], section.Tags);
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)))
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void TryLoad_DamagedFile_ReturnsFalseAndKeepsDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), "EngineTests", Guid.NewGuid().ToString("N"), "broken.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not json ]");
        try
        {
            EngineConfiguration configuration = new();
            Assert.False(configuration.TryLoad(path));
            Assert.Equal("Engine", configuration.GetSection<DisplaySection>("Display").Title);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
