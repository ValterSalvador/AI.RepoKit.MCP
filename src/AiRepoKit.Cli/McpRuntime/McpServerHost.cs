using AiRepoKit.Cli.McpRuntime.Prompts;
using AiRepoKit.Cli.McpRuntime.Resources;
using AiRepoKit.Cli.McpRuntime.Services;
using AiRepoKit.Cli.McpRuntime.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace AiRepoKit.Cli.McpRuntime;

public static class McpServerHost
{
    public static string DefaultLogFile => Path.Combine(Path.GetTempPath(), "ai-repo-context-mcp.log");

    public static IHost CreateHost(string repoRoot_, bool stderrLogging_ = false)
    {
        string resolvedLogFile = DefaultLogFile;

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            ContentRootPath = Directory.GetCurrentDirectory(),
            DisableDefaults = true
        });

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(stderrLogging_ ? LogLevel.Trace : LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        builder.Logging.AddFilter("ModelContextProtocol.Server", LogLevel.Warning);
        builder.Logging.AddProvider(new FileLoggerProvider(resolvedLogFile));

        if (stderrLogging_)
        {
            builder.Logging.AddFilter<ConsoleLoggerProvider>("Microsoft.Hosting.Lifetime", LogLevel.Information);
            builder.Logging.AddFilter<ConsoleLoggerProvider>("ModelContextProtocol.Server", LogLevel.Information);
            builder.Logging.AddConsole(options_ => options_.LogToStandardErrorThreshold = LogLevel.Trace);
        }

        builder.Services.AddSingleton(new ContextRepositoryOptions(Path.GetFullPath(repoRoot_)));
        builder.Services.AddSingleton<SecretRedactor>();
        builder.Services.AddSingleton<ContextRepository>();
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<RepositoryContextTools>()
            .WithResources<RepositoryContextResources>()
            .WithPrompts<RepositoryContextPrompts>();

        return builder.Build();
    }

    public static async Task<int> RunAsync(string repoRoot_, bool stderrLogging_ = false, CancellationToken cancellationToken_ = default)
    {
        string resolvedLogFile = DefaultLogFile;
        try
        {
            using IHost host = CreateHost(repoRoot_, stderrLogging_);
            await host.RunAsync(cancellationToken_).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                File.AppendAllText(resolvedLogFile, $"{DateTimeOffset.UtcNow:O} startup failure: {exception}{Environment.NewLine}");
            }
            catch
            {
            }

            if (stderrLogging_)
            {
                Console.Error.WriteLine(exception);
            }

            return 1;
        }
    }
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
