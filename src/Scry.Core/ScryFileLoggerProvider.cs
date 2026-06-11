using Microsoft.Extensions.Logging;

namespace Scry.Core;

/// <summary>
/// A simple <see cref="ILoggerProvider"/> that writes log lines to a single file.
/// Thread-safe: a <see cref="Lock"/> guards all writes.
/// </summary>
public sealed class ScryFileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly LogLevel _minLevel;
    private readonly Lock _gate = new();

    /// <summary>
    /// Creates the directory and opens <paramref name="filePath"/> for exclusive writing.
    /// </summary>
    public ScryFileLoggerProvider(string filePath, LogLevel minLevel)
    {
        _minLevel = minLevel;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(fs) { AutoFlush = true };
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(categoryName, _writer, _minLevel, _gate);

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }

    private sealed class FileLogger(string _category, StreamWriter _writer, LogLevel _minLevel, Lock _gate) : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= _minLevel && logLevel != LogLevel.None;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var level = logLevel switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "???",
            };

            lock (_gate)
            {
                _writer.WriteLine($"{DateTimeOffset.Now:o} [{level}] {_category}: {message}");
                if (exception is not null)
                {
                    _writer.WriteLine(exception.ToString());
                }
            }
        }
    }
}
