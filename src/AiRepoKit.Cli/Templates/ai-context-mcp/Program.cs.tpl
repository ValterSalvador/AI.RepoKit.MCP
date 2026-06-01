using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using {{McpNamespace}}.Services;
using {{McpNamespace}}.Tools;

string repoRoot = Directory.GetCurrentDirectory();
bool stderrLogging = false;
string logFile = Path.Combine(Path.GetTempPath(), "ai-repo-context-mcp.log");
for (int index = 0; index < args.Length; index++)
{
    if (string.Equals(args[index], "--repo", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
    {
        repoRoot = args[index + 1];
    }

    if (string.Equals(args[index], "--debug", StringComparison.OrdinalIgnoreCase)
        || string.Equals(args[index], "--verbose", StringComparison.OrdinalIgnoreCase))
    {
        stderrLogging = true;
    }

    if (string.Equals(args[index], "--log-file", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
    {
        logFile = args[index + 1];
    }
}

try
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = [],
        ContentRootPath = Directory.GetCurrentDirectory(),
        DisableDefaults = true
    });
    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(stderrLogging ? LogLevel.Trace : LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
    builder.Logging.AddFilter("ModelContextProtocol.Server", LogLevel.Warning);
    builder.Logging.AddProvider(new FileLoggerProvider(logFile));
    if (stderrLogging)
    {
        builder.Logging.AddFilter<ConsoleLoggerProvider>("Microsoft.Hosting.Lifetime", LogLevel.Information);
        builder.Logging.AddFilter<ConsoleLoggerProvider>("ModelContextProtocol.Server", LogLevel.Information);
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    }

    builder.Services.AddSingleton(new ContextRepositoryOptions(Path.GetFullPath(repoRoot)));
    builder.Services.AddSingleton<SecretRedactor>();
    builder.Services.AddSingleton<ContextRepository>();
    builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<RepositoryContextTools>();
    await builder.Build().RunAsync();
}
catch (Exception exception)
{
    try
    {
        File.AppendAllText(logFile, $"{DateTimeOffset.UtcNow:O} startup failure: {exception}{Environment.NewLine}");
    }
    catch
    {
    }

    if (stderrLogging)
    {
        Console.Error.WriteLine(exception);
    }

    Environment.ExitCode = 1;
}

internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;

    public FileLoggerProvider(string path_)
    {
        this._path = Path.GetFullPath(path_);
    }

    public ILogger CreateLogger(string categoryName_)
    {
        return new FileLogger(this._path, categoryName_);
    }

    public void Dispose()
    {
    }
}

internal sealed class FileLogger : ILogger
{
    private readonly string _path;
    private readonly string _categoryName;

    public FileLogger(string path_, string categoryName_)
    {
        this._path = path_;
        this._categoryName = categoryName_;
    }

    public IDisposable? BeginScope<TState>(TState state_) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel_)
    {
        return logLevel_ >= LogLevel.Information;
    }

    public void Log<TState>(LogLevel logLevel_, EventId eventId_, TState state_, Exception? exception_, Func<TState, Exception?, string> formatter_)
    {
        if (!this.IsEnabled(logLevel_))
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(this._path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string message = formatter_(state_, exception_);
            File.AppendAllText(this._path, $"{DateTimeOffset.UtcNow:O} {logLevel_} {this._categoryName}: {message}{Environment.NewLine}");
            if (exception_ is not null)
            {
                File.AppendAllText(this._path, exception_ + Environment.NewLine);
            }
        }
        catch
        {
        }
    }
}
