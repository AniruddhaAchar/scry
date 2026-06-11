using Microsoft.Extensions.Logging;
using Scry.Core;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class ScryLoggingTests
{
    [Fact]
    public void Resolve_Verbose_ForcesDebugLevel()
    {
        var cfg = new ScryConfig();
        var r = ScryLogging.Resolve("scry", verbose: true, cfg);
        Assert.Equal(LogLevel.Debug, r.Level);
    }

    [Fact]
    public void Resolve_ConfigLevel_HonoredWhenNotVerbose()
    {
        var cfg = new ScryConfig { Logging = new LoggingConfig { Level = "Warning" } };
        var r = ScryLogging.Resolve("scry", verbose: false, cfg);
        Assert.Equal(LogLevel.Warning, r.Level);
    }

    [Fact]
    public void Resolve_DefaultLevel_IsWarning()
    {
        var cfg = new ScryConfig();
        var r = ScryLogging.Resolve("scry", verbose: false, cfg);
        Assert.Equal(LogLevel.Warning, r.Level);
    }

    [Fact]
    public void Resolve_DefaultFolder_IsDefaultLogsDir()
    {
        var cfg = new ScryConfig();
        var r = ScryLogging.Resolve("scry", verbose: false, cfg);
        Assert.Equal(ScryPaths.DefaultLogsDir, r.Folder);
    }

    [Fact]
    public void Resolve_CustomFolder_IsUsed()
    {
        var cfg = new ScryConfig { Logging = new LoggingConfig { Folder = "/custom/logs" } };
        var r = ScryLogging.Resolve("scry", verbose: false, cfg);
        Assert.Equal("/custom/logs", r.Folder);
    }

    [Fact]
    public void Resolve_FilePath_ContainsAppNameAndPid_AndEndsWithLog()
    {
        var cfg = new ScryConfig();
        var r = ScryLogging.Resolve("scryd", verbose: false, cfg);
        Assert.Contains("scryd", r.FilePath);
        Assert.Contains(Environment.ProcessId.ToString(), r.FilePath);
        Assert.EndsWith(".log", r.FilePath);
    }
}
