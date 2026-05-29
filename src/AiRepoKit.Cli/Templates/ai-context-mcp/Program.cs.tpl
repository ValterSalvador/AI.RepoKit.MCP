using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using {{McpNamespace}}.Services;
using {{McpNamespace}}.Tools;

string repoRoot = Directory.GetCurrentDirectory();
for (int index = 0; index < args.Length; index++)
{
    if (string.Equals(args[index], "--repo", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
    {
        repoRoot = args[index + 1];
    }
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(new ContextRepositoryOptions(Path.GetFullPath(repoRoot)));
builder.Services.AddSingleton<SecretRedactor>();
builder.Services.AddSingleton<ContextRepository>();
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<RepositoryContextTools>();
await builder.Build().RunAsync();
