using Scry.Contracts;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class ScrySessionsTests : IDisposable
{
    private readonly string _dir;
    private readonly string? _previous;

    public ScrySessionsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "scry-test-sessions-" + Guid.NewGuid().ToString("N"));
        _previous = Environment.GetEnvironmentVariable("SCRY_SESSIONS_DIR");
        Environment.SetEnvironmentVariable("SCRY_SESSIONS_DIR", _dir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SCRY_SESSIONS_DIR", _previous);
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Register_ThenList_ReturnsSameSession()
    {
        var d = new SessionDescriptor(
            "scry-aabbccdd11223344",
            @"C:\dumps\app.dmp",
            Environment.ProcessId,   // alive: us
            DateTimeOffset.UtcNow,
            "0.0.1");

        ScrySessions.Register(d);

        var list = ScrySessions.List();
        Assert.Single(list);
        Assert.Equal(d.Handle, list[0].Handle);
        Assert.Equal(d.DumpPath, list[0].DumpPath);
    }

    [Fact]
    public void List_PrunesDeadPid_AndRemovesFile()
    {
        var d = new SessionDescriptor(
            "scry-deadbeef00000000",
            @"C:\dumps\dead.dmp",
            int.MaxValue,            // no such pid
            DateTimeOffset.UtcNow,
            "0.0.1");

        ScrySessions.Register(d);
        Assert.True(File.Exists(Path.Combine(_dir, d.Handle + ".json")));

        var list = ScrySessions.List();
        Assert.Empty(list);

        // File should have been removed during the list/prune pass.
        Assert.False(File.Exists(Path.Combine(_dir, d.Handle + ".json")));
    }

    [Fact]
    public void Unregister_RemovesFile()
    {
        var d = new SessionDescriptor(
            "scry-1122334455667788",
            @"C:\dumps\remove.dmp",
            Environment.ProcessId,
            DateTimeOffset.UtcNow,
            "0.0.1");

        ScrySessions.Register(d);
        Assert.True(File.Exists(Path.Combine(_dir, d.Handle + ".json")));

        ScrySessions.Unregister(d.Handle);
        Assert.False(File.Exists(Path.Combine(_dir, d.Handle + ".json")));
    }

    [Fact]
    public void List_ReturnsEmpty_WhenRegistryDirMissing()
    {
        // Dir does not exist (Dispose will clean up but it was never created).
        Assert.False(Directory.Exists(_dir));
        var list = ScrySessions.List();
        Assert.Empty(list);
    }

    [Fact]
    public void Unregister_IsIdempotent_WhenFileMissing()
    {
        // Should not throw even when the file never existed.
        ScrySessions.Unregister("scry-nonexistent0000");
    }
}
