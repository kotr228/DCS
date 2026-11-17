using System.Text;
using System.Threading.Channels;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace AsmodayCat.Service;

// Daily log files: %LOCALAPPDATA%\CatSuite\AsmodayCat\logs\service-yyyyMMdd.log (kept 14 days).
// Writes happen on a background task so logging never blocks the caller.
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int KeepDays = 14;

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatSuite", "AsmodayCat", "logs");

    private readonly Channel<string> _lines = Channel.CreateBounded<string>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Task _writer;

    public FileLoggerProvider()
    {
        Directory.CreateDirectory(LogDirectory);
        DeleteOldFiles();
        _writer = Task.Run(WriteLoopAsync);
    }

    // Written synchronously: used when the process may be about to die
    public static void WriteCrash(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(Path.Combine(LogDirectory, $"service-{DateTime.Now:yyyyMMdd}.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [CRT] Unhandled: {ex}{Environment.NewLine}", Encoding.UTF8);
        }
        catch (Exception) { /* nothing left to do */ }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _lines.Writer);

    private async Task WriteLoopAsync()
    {
        await foreach (var line in _lines.Reader.ReadAllAsync())
        {
            try
            {
                var path = Path.Combine(LogDirectory, $"service-{DateTime.Now:yyyyMMdd}.log");
                await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException) { /* file locked by a viewer: drop the line */ }
        }
    }

    private static void DeleteOldFiles()
    {
        try
        {
            foreach (var f in new DirectoryInfo(LogDirectory).EnumerateFiles("*.log"))
                if (f.LastWriteTime < DateTime.Now.AddDays(-KeepDays)) f.Delete();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _lines.Writer.TryComplete();
        _writer.Wait(TimeSpan.FromSeconds(2));
    }

    private sealed class FileLogger(string category, ChannelWriter<string> lines) : ILogger
    {
        private readonly string _source = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(MsLogLevel logLevel) => logLevel >= MsLogLevel.Debug;

        public void Log<TState>(MsLogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var level = logLevel switch
            {
                MsLogLevel.Debug    => "DBG",
                MsLogLevel.Information => "INF",
                MsLogLevel.Warning  => "WRN",
                MsLogLevel.Error    => "ERR",
                MsLogLevel.Critical => "CRT",
                _                   => "TRC"
            };
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {_source}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            lines.TryWrite(line);
        }
    }
}
