using System.Text.Json;
using System.Text.Json.Serialization;
using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services.BuildDiagnostics;

public sealed class BuildDiagnosticsService :
    IBuildDiagnosticsService
{
    public const string ReportRelativePath =
        ".ai/generated/reports/build-diagnostics-report.json";

    public const string SummaryRelativePath =
        ".ai/generated/reports/latest-build-summary.json";

    private const int RestoreTailLimit = 80;
    private const int BuildTailLimit = 120;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull
        };

    private readonly IProcessRunner _processRunner;

    public BuildDiagnosticsService()
        : this(new ProcessRunner())
    {
    }

    public BuildDiagnosticsService(
        IProcessRunner processRunner)
    {
        _processRunner =
            processRunner ??
            throw new ArgumentNullException(
                nameof(processRunner));
    }

    public BuildDiagnosticsRunResult Run(
        string repoRoot)
    {
        if (string.IsNullOrWhiteSpace(
                repoRoot))
        {
            return BuildDiagnosticsRunResult.Failure(
                "Repository root path cannot be empty.");
        }

        string root;

        try
        {
            root =
                Path.GetFullPath(
                    repoRoot);
        }
        catch (Exception exception)
        {
            return BuildDiagnosticsRunResult.Failure(
                ProcessRunner.Redact(
                    exception.Message));
        }

        if (!Directory.Exists(root))
        {
            return BuildDiagnosticsRunResult.Failure(
                "Repository root path was not found.");
        }

        try
        {
            string reportsRoot =
                Path.Combine(
                    root,
                    ".ai",
                    "generated",
                    "reports");

            Directory.CreateDirectory(
                reportsRoot);

            string? solution =
                Directory
                    .EnumerateFiles(
                        root,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .Where(
                        path =>
                            Path.GetExtension(path)
                                .StartsWith(
                                    ".sln",
                                    StringComparison.OrdinalIgnoreCase))
                    .OrderBy(
                        path =>
                            Path.GetFileName(path),
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        path =>
                            Path.GetFileName(path),
                        StringComparer.Ordinal)
                    .FirstOrDefault();

            string generatedAtLocal =
                DateTimeOffset.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss zzz");

            if (solution is null)
            {
                BuildDiagnosticsReport noSolutionReport =
                    new()
                    {
                        GeneratedAtLocal =
                            generatedAtLocal,
                        Target =
                            string.Empty,
                        RestoreExitCode =
                            0,
                        BuildExitCode =
                            0,
                        Status =
                            "No root solution found."
                    };

                string json =
                    Serialize(
                        noSolutionReport);

                WriteJson(
                    GetReportPath(
                        root),
                    json);

                WriteJson(
                    GetSummaryPath(
                        root),
                    json);

                return BuildDiagnosticsRunResult.Complete(
                    noSolutionReport,
                    0);
            }

            ProcessResult restore =
                _processRunner.Run(
                    "dotnet",
                    [
                        "restore",
                        solution
                    ],
                    root);

            ProcessResult build =
                _processRunner.Run(
                    "dotnet",
                    [
                        "build",
                        solution,
                        "-c",
                        "Debug",
                        "--no-restore"
                    ],
                    root);

            BuildDiagnosticsReport report =
                new()
                {
                    GeneratedAtLocal =
                        generatedAtLocal,
                    Target =
                        Path.GetFileName(
                            solution),
                    RestoreExitCode =
                        restore.ExitCode,
                    BuildExitCode =
                        build.ExitCode,
                    RestoreOutputTail =
                        GetOutputTail(
                            restore,
                            RestoreTailLimit),
                    BuildOutputTail =
                        GetOutputTail(
                            build,
                            BuildTailLimit)
                };

            WriteJson(
                GetReportPath(
                    root),
                Serialize(
                    report));

            object summary =
                new
                {
                    generatedAtLocal =
                        report.GeneratedAtLocal,
                    target =
                        report.Target,
                    restoreExitCode =
                        report.RestoreExitCode,
                    buildExitCode =
                        report.BuildExitCode
                };

            WriteJson(
                GetSummaryPath(
                    root),
                Serialize(
                    summary));

            int exitCode =
                restore.ExitCode != 0
                    ? restore.ExitCode
                    : build.ExitCode;

            return BuildDiagnosticsRunResult.Complete(
                report,
                exitCode);
        }
        catch (Exception exception)
        {
            return BuildDiagnosticsRunResult.Failure(
                ProcessRunner.Redact(
                    exception.Message));
        }
    }

    private static IReadOnlyList<string> GetOutputTail(
        ProcessResult process,
        int limit)
    {
        return SplitLines(
                process.StandardOutput)
            .Concat(
                SplitLines(
                    process.StandardError))
            .TakeLast(
                limit)
            .ToArray();
    }

    private static IReadOnlyList<string> SplitLines(
        string value)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return [];
        }

        string normalized =
            value
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n');

        string[] lines =
            normalized.Split(
                '\n',
                StringSplitOptions.None);

        if (lines.Length > 0 &&
            lines[^1].Length == 0)
        {
            return lines[..^1];
        }

        return lines;
    }

    private static string GetReportPath(
        string root)
    {
        return Path.Combine(
            root,
            ReportRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar));
    }

    private static string GetSummaryPath(
        string root)
    {
        return Path.Combine(
            root,
            SummaryRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar));
    }

    private static string Serialize(
        object value)
    {
        return JsonSerializer.Serialize(
            value,
            JsonOptions);
    }

    private static void WriteJson(
        string path,
        string json)
    {
        File.WriteAllText(
            path,
            json +
            Environment.NewLine);
    }
}
