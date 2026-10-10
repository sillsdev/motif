using Microsoft.Extensions.Logging;

namespace SIL.Motif.Mcp;

internal sealed class StderrLoggerFactory(TextWriter writer) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new StderrLogger(writer, categoryName);

    public void AddProvider(ILoggerProvider provider) =>
        throw new NotSupportedException("MCP server diagnostics use the supplied standard-error writer.");

    public void Dispose() { }

    private sealed class StderrLogger(TextWriter writer, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => EmptyScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            lock (writer)
            {
                writer.WriteLine($"{logLevel} {categoryName}: {formatter(state, exception)}");
                if (exception is not null) writer.WriteLine(exception);
                writer.Flush();
            }
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();

        public void Dispose() { }
    }
}
