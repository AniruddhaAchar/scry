using Scry.Core;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class ScryConfigTests
{
    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var cfg = ScryConfig.Load(Path.Combine(Path.GetTempPath(), "nonexistent-scry-config-" + Guid.NewGuid() + ".json"));
        Assert.NotNull(cfg);
        Assert.NotNull(cfg.Logging);
        Assert.Null(cfg.Logging.Folder);
        Assert.Null(cfg.Logging.Level);
    }

    [Fact]
    public void Load_ValidJson_PopulatesFields()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{ "logging": { "folder": "/tmp/logs", "level": "Debug" } }""");
            var cfg = ScryConfig.Load(path);
            Assert.Equal("/tmp/logs", cfg.Logging.Folder);
            Assert.Equal("Debug", cfg.Logging.Level);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MalformedJson_ReturnsDefaults()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "this is not json {{{");
            var cfg = ScryConfig.Load(path);
            Assert.NotNull(cfg);
            Assert.Null(cfg.Logging.Folder);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
