using AsmodayCat.Shared.Models;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace AsmodayCat.Service;

public sealed class TerminalLoggerProvider : ILoggerProvider
{
    private readonly TerminalLogStore _store;

    public TerminalLoggerProvider(TerminalLogStore store) => _store = store;

    public ILogger CreateLogger(string categoryName) =>
        new TerminalLogger(categoryName, _store);

    public void Dispose() { }

    private sealed class TerminalLogger : ILogger
    {
        private readonly string           _source;
        private readonly TerminalLogStore _store;

        public TerminalLogger(string category, TerminalLogStore store)
        {
            _source = category.Contains('.')
                ? category[(category.LastIndexOf('.') + 1)..]
                : category;
            _store = store;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(MsLogLevel logLevel) => logLevel >= MsLogLevel.Information;

        public void Log<TState>(
            MsLogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var level = logLevel switch
            {
                MsLogLevel.Warning  => LogEntryLevel.Warning,
                MsLogLevel.Error    => LogEntryLevel.Error,
                MsLogLevel.Critical => LogEntryLevel.Error,
                _                   => LogEntryLevel.Info
            };

            var msg = formatter(state, exception);
            if (exception != null) msg += $" — {exception.Message}";

            _store.Add(new LogEntryDto { Level = level, Message = msg, Source = _source });
        }
    }
}
