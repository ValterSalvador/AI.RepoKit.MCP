using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Models.McpDiagnostics;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.McpLaunch;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class PortableMcpSpecProtocolTests
{
    [Fact]
    public void McpSmokeTestService_ExpandedDepth_ExecutesP08ContextCallsWithoutProtocolError()
    {
        using TempDir repo = new();
        McpServerLaunchSpec launchSpec = McpServerLaunchSpecResolver.ResolvePortable(repo.Path);

        McpSmokeTestService smokeTestService = new();
        McpSmokeTestResult result = smokeTestService.Run(
            launchSpec,
            verbose_: true,
            strictStdio_: false,
            depth_: McpSmokeTestDepth.Expanded);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Status is "Passed" or "Warning");
        Assert.Equal(5, result.ToolNames.Count);
        Assert.Contains("get_context", result.ToolNames);
        Assert.Contains("get_health", result.ToolNames);
        Assert.Contains("get_policy", result.ToolNames);
        Assert.Contains("get_repo_brief", result.ToolNames);
        Assert.Contains("search_context", result.ToolNames);
    }

    [Fact]
    public void StdioSession_WithControlledFixture_ReturnsSuccessfulP08Contexts()
    {
        using TempDir repo = new();
        const string specId = "protocol-spec";

        // Setup canonical spec and approvals
        RequirementSet rs = new()
        {
            Revision = new ArtifactRevision(1),
            Inputs = [new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input" }],
            Requirements = [new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Req", SourceInputIds = [new StableEntityId("INPUT-001")] }]
        };
        WorkSpec ws = new()
        {
            Revision = new ArtifactRevision(1),
            RequirementSetRevision = new ArtifactRevision(1),
            Constraints = [],
            AcceptanceCriteria = []
        };
        ImplementationPlan plan = new()
        {
            Revision = new ArtifactRevision(1),
            WorkSpecRevision = new ArtifactRevision(1),
            Steps = [new PlanStep { Id = new StableEntityId("PLAN-STEP-001"), Statement = "Step", RequirementIds = [new StableEntityId("REQ-001")], AcceptanceCriterionIds = [] }]
        };
        SpecApprovalLedger ledger = new()
        {
            SpecId = specId,
            Approvals =
            [
                new Approval
                {
                    Id = new StableEntityId("APR-001"),
                    ArtifactKind = SpecArtifactKind.RequirementSet,
                    ArtifactIdentity = SpecArtifactIdentity.RequirementSet,
                    ArtifactRevision = new ArtifactRevision(1),
                    CanonicalSemanticRepresentation = SpecSemanticCanonicalizer.Canonicalize(rs),
                    SemanticDigest = SpecSemanticDigest.Compute(rs)
                },
                new Approval
                {
                    Id = new StableEntityId("APR-002"),
                    ArtifactKind = SpecArtifactKind.WorkSpec,
                    ArtifactIdentity = SpecArtifactIdentity.WorkSpec,
                    ArtifactRevision = new ArtifactRevision(1),
                    CanonicalSemanticRepresentation = SpecSemanticCanonicalizer.Canonicalize(ws),
                    SemanticDigest = SpecSemanticDigest.Compute(ws)
                },
                new Approval
                {
                    Id = new StableEntityId("APR-003"),
                    ArtifactKind = SpecArtifactKind.ImplementationPlan,
                    ArtifactIdentity = SpecArtifactIdentity.ImplementationPlan,
                    ArtifactRevision = new ArtifactRevision(1),
                    CanonicalSemanticRepresentation = SpecSemanticCanonicalizer.Canonicalize(plan),
                    SemanticDigest = SpecSemanticDigest.Compute(plan)
                }
            ]
        };

        string specDir = Path.Combine(repo.Path, ".ai", "specs", specId);
        Directory.CreateDirectory(specDir);
        File.WriteAllText(Path.Combine(specDir, "requirements.json"), SpecJsonSerializer.Serialize(rs));
        File.WriteAllText(Path.Combine(specDir, "work-spec.json"), SpecJsonSerializer.Serialize(ws));
        File.WriteAllText(Path.Combine(specDir, "implementation-plan.json"), SpecJsonSerializer.Serialize(plan));
        File.WriteAllText(Path.Combine(specDir, "approvals.json"), SpecJsonSerializer.Serialize(ledger));

        // Setup SpecContext
        SpecContext ctx = new()
        {
            SpecId = specId,
            RequirementSetRevision = new ArtifactRevision(1),
            WorkSpecRevision = new ArtifactRevision(1),
            Target = "repository",
            ReferenceLimit = 10,
            Budget = 1000,
            EstimatedTokens = 100,
            Evidence =
            [
                new RepositoryEvidence
                {
                    EvidenceId = "evidence-1",
                    Source = "repository",
                    Kind = "file",
                    Reference = "README.md",
                    Availability = RepositoryEvidenceAvailability.Available,
                    Freshness = RepositoryEvidenceFreshness.Current,
                    SourceGeneratedAt = "2026-09-03T00:00:00Z",
                    Detail = "Overview"
                }
            ],
            References =
            [
                new SpecContextReference
                {
                    EvidenceId = "evidence-1",
                    Kind = "file",
                    Reference = "README.md",
                    Reason = "Relevant",
                    Priority = 1
                }
            ],
            Omissions = []
        };
        string ctxDir = Path.Combine(repo.Path, ".ai", "generated", "spec-context");
        Directory.CreateDirectory(ctxDir);
        File.WriteAllText(Path.Combine(ctxDir, $"{specId}.json"), SpecJsonSerializer.Serialize(ctx));

        // Launch portable server process
        McpServerLaunchSpec launchSpec = McpServerLaunchSpecResolver.ResolvePortable(repo.Path);
        using Process process = new();
        process.StartInfo.FileName = launchSpec.FileName;
        process.StartInfo.WorkingDirectory = launchSpec.WorkingDirectory;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        foreach (string argument in launchSpec.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        List<string> stdoutLines = [];
        List<string> stderrLines = [];
        process.OutputDataReceived += (_, args_) =>
        {
            if (args_.Data is not null)
            {
                lock (stdoutLines) { stdoutLines.Add(args_.Data); }
            }
        };
        process.ErrorDataReceived += (_, args_) =>
        {
            if (args_.Data is not null)
            {
                lock (stderrLines) { stderrLines.Add(args_.Data); }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            // 1. Initialize
            WriteJson(process, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "test-client", version = "1.0.0" }
                }
            });
            using JsonDocument initRes = WaitForResponse(stdoutLines, 1, TimeSpan.FromSeconds(15));
            Assert.False(initRes.RootElement.TryGetProperty("error", out _));

            WriteJson(process, new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized",
                @params = new { }
            });

            // 2. get_context kind=spec target=protocol-spec
            WriteJson(process, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new
                {
                    name = "get_context",
                    arguments = new { kind = "spec", target = specId, detail = "brief" }
                }
            });
            using JsonDocument specRes = WaitForResponse(stdoutLines, 2, TimeSpan.FromSeconds(15));
            Assert.False(specRes.RootElement.TryGetProperty("error", out _));
            string specStr = specRes.RootElement.GetProperty("result").ToString();
            Assert.Contains("protocol-spec", specStr);
            Assert.Contains("current", specStr);

            // 3. get_context kind=spec-context target=protocol-spec
            WriteJson(process, new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new
                {
                    name = "get_context",
                    arguments = new { kind = "spec-context", target = specId, detail = "brief" }
                }
            });
            using JsonDocument ctxRes = WaitForResponse(stdoutLines, 3, TimeSpan.FromSeconds(15));
            Assert.False(ctxRes.RootElement.TryGetProperty("error", out _));
            string ctxStr = ctxRes.RootElement.GetProperty("result").ToString();
            Assert.Contains("protocol-spec", ctxStr);
            Assert.Contains("evidenceCount", ctxStr);

            // 4. get_context kind=verification target=protocol-spec
            WriteJson(process, new
            {
                jsonrpc = "2.0",
                id = 4,
                method = "tools/call",
                @params = new
                {
                    name = "get_context",
                    arguments = new { kind = "verification", target = specId, detail = "brief" }
                }
            });
            using JsonDocument verRes = WaitForResponse(stdoutLines, 4, TimeSpan.FromSeconds(15));
            Assert.False(verRes.RootElement.TryGetProperty("error", out _));
            string verStr = verRes.RootElement.GetProperty("result").ToString();
            Assert.Contains("graphReady", verStr);
            Assert.Contains("true", verStr);
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
                process.WaitForExit(2000);
            }
            catch
            {
            }
        }
    }

    private static void WriteJson(Process process_, object value_)
    {
        process_.StandardInput.WriteLine(JsonSerializer.Serialize(value_));
        process_.StandardInput.Flush();
    }

    private static JsonDocument WaitForResponse(List<string> stdoutLines_, int id_, TimeSpan timeout_)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout_)
        {
            lock (stdoutLines_)
            {
                for (int i = 0; i < stdoutLines_.Count; i++)
                {
                    string line = stdoutLines_[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        JsonDocument doc = JsonDocument.Parse(line);
                        if (doc.RootElement.TryGetProperty("id", out JsonElement idElem) && idElem.GetInt32() == id_)
                        {
                            return doc;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            Thread.Sleep(50);
        }

        throw new TimeoutException($"Timed out waiting for response ID {id_}.");
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "airepo-proto-test-" + Guid.NewGuid().ToString("N"));

        public TempDir()
        {
            Directory.CreateDirectory(this.Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(this.Path))
                {
                    Directory.Delete(this.Path, true);
                }
            }
            catch
            {
            }
        }
    }
}
