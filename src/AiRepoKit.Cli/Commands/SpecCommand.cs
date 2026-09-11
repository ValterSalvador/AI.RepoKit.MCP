using AiRepoKit.Cli.Commands.Spec;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;

namespace AiRepoKit.Cli.Commands;

public sealed class SpecCommand
{
    public CommandResult Execute(IReadOnlyList<string> arguments_)
    {
        if (arguments_.Count == 0)
        {
            return CommandResult.Ok(SpecCommandRenderer.GetUsage());
        }

        string subcommand = arguments_[0];
        bool isHelp = string.Equals(subcommand, "help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(subcommand, "--help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(subcommand, "-h", StringComparison.OrdinalIgnoreCase);

        if (isHelp)
        {
            if (arguments_.Count == 1)
            {
                return CommandResult.Ok(SpecCommandRenderer.GetUsage());
            }

            return CommandResult.Failure(
                $"# Spec Command Error{Environment.NewLine}{Environment.NewLine}Unexpected argument(s) after Spec help: `{string.Join("`, `", arguments_.Skip(1))}`.{Environment.NewLine}{Environment.NewLine}{SpecCommandRenderer.GetUsage()}",
                1);
        }

        return subcommand.ToLowerInvariant() switch
        {
            "init" => this.ExecuteInit(arguments_.Skip(1).ToArray()),
            "show" => this.ExecuteShow(arguments_.Skip(1).ToArray()),
            "refine" => this.ExecuteRefine(arguments_.Skip(1).ToArray()),
            "approve" => this.ExecuteApprove(arguments_.Skip(1).ToArray()),
            _ => this.HandleUnknownSubcommand(subcommand, arguments_)
        };
    }

    private CommandResult HandleUnknownSubcommand(string subcommand_, IReadOnlyList<string> arguments_)
    {
        bool isJson = arguments_.Any(arg_ => string.Equals(arg_, "--json", StringComparison.OrdinalIgnoreCase));
        string error = $"Unsupported Spec subcommand: `{subcommand_}`.";
        return SpecCommandRenderer.RenderError(error, isJson, includeUsage_: true);
    }

    private CommandResult ExecuteInit(IReadOnlyList<string> args_)
    {
        SpecInitOptions options;
        try
        {
            options = SpecCommandParser.ParseInit(args_);
        }
        catch (SpecCliParsingException exception)
        {
            return SpecCommandRenderer.RenderError(exception.Message, exception.IsJson);
        }

        string repoRoot;
        try
        {
            repoRoot = ResolveRepo(options.RepoPath);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Repository path resolution failed: " + exception.Message, options.IsJson);
        }

        RequirementSet candidate;
        try
        {
            candidate = SpecCommandInputReader.ReadCandidate<RequirementSet>(options.FromPath, SpecArtifactKind.RequirementSet);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(exception, options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Failed to read candidate artifact: " + exception.Message, options.IsJson);
        }

        try
        {
            SpecLifecycleService service = new(repoRoot, options.SpecId);
            SpecStoreResult result = service.InitializeRequirementSet(
                candidate,
                new SpecStoreOptions
                {
                    Mode = options.Mode
                });

            return SpecCommandRenderer.RenderInitResult(options.SpecId, result, options.IsJson);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(exception, options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Spec init failed: " + exception.Message, options.IsJson);
        }
    }

    private CommandResult ExecuteShow(IReadOnlyList<string> args_)
    {
        SpecShowOptions options;
        try
        {
            options = SpecCommandParser.ParseShow(args_);
        }
        catch (SpecCliParsingException exception)
        {
            return SpecCommandRenderer.RenderError(exception.Message, exception.IsJson);
        }

        string repoRoot;
        try
        {
            repoRoot = ResolveRepo(options.RepoPath);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Repository path resolution failed: " + exception.Message, options.IsJson);
        }

        try
        {
            SpecLifecycleService service = new(repoRoot, options.SpecId);
            SpecWorkspaceSnapshot snapshot = service.Workspace.Load();
            SpecApprovalLedger? ledger = service.LedgerStore.Load();
            IReadOnlyList<SpecArtifactApprovalStatus> approvalStatuses = service.GetApprovalStatuses();

            return SpecCommandRenderer.RenderShowResult(
                options.SpecId,
                options.ArtifactSelector,
                snapshot,
                ledger,
                approvalStatuses,
                options.IsJson);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(exception, options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Spec show failed: " + exception.Message, options.IsJson);
        }
    }

    private CommandResult ExecuteRefine(IReadOnlyList<string> args_)
    {
        SpecRefineOptions options;
        try
        {
            options = SpecCommandParser.ParseRefine(args_);
        }
        catch (SpecCliParsingException exception)
        {
            return SpecCommandRenderer.RenderError(exception.Message, exception.IsJson);
        }

        string repoRoot;
        try
        {
            repoRoot = ResolveRepo(options.RepoPath);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Repository path resolution failed: " + exception.Message, options.IsJson);
        }

        try
        {
            SpecLifecycleService service = new(repoRoot, options.SpecId);
            SpecWorkspaceSnapshot snapshot = service.Workspace.Load();
            SpecStoreResult result;

            if (options.Artifact == "requirements")
            {
                if (options.ExpectedRevision is null)
                {
                    return SpecCommandRenderer.RenderError("Refining RequirementSet requires '--expected-revision'.", options.IsJson);
                }

                if (snapshot.RequirementSet is not null && snapshot.RequirementSet.Revision != options.ExpectedRevision.Value)
                {
                    throw new SpecPersistenceException(
                        SpecPersistenceException.RevisionConflict,
                        $"The expected current revision '{options.ExpectedRevision.Value.Value}' does not match the canonical RequirementSet revision '{snapshot.RequirementSet.Revision.Value}'.",
                        SpecArtifactKind.RequirementSet);
                }

                RequirementSet candidate = SpecCommandInputReader.ReadCandidate<RequirementSet>(options.FromPath, SpecArtifactKind.RequirementSet);
                result = service.RefineRequirementSet(
                    candidate,
                    new SpecStoreOptions
                    {
                        Mode = options.Mode,
                        ExpectedCurrentRevision = options.ExpectedRevision
                    });
            }
            else
            {
                bool workSpecExists = snapshot.WorkSpec is not null;
                if (!workSpecExists)
                {
                    if (options.ExpectedRevision is not null)
                    {
                        return SpecCommandRenderer.RenderError("Cannot specify '--expected-revision' when creating the initial WorkSpec.", options.IsJson);
                    }
                }
                else
                {
                    if (options.ExpectedRevision is null)
                    {
                        return SpecCommandRenderer.RenderError("Refining existing WorkSpec requires '--expected-revision'.", options.IsJson);
                    }

                    if (snapshot.WorkSpec!.Revision != options.ExpectedRevision.Value)
                    {
                        throw new SpecPersistenceException(
                            SpecPersistenceException.RevisionConflict,
                            $"The expected current revision '{options.ExpectedRevision.Value.Value}' does not match the canonical WorkSpec revision '{snapshot.WorkSpec!.Revision.Value}'.",
                            SpecArtifactKind.WorkSpec);
                    }
                }

                WorkSpec candidate = SpecCommandInputReader.ReadCandidate<WorkSpec>(options.FromPath, SpecArtifactKind.WorkSpec);
                result = service.RefineWorkSpec(
                    candidate,
                    new SpecStoreOptions
                    {
                        Mode = options.Mode,
                        ExpectedCurrentRevision = options.ExpectedRevision
                    });
            }

            return SpecCommandRenderer.RenderRefineResult(options.SpecId, result, options.IsJson);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(exception, options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Spec refine failed: " + exception.Message, options.IsJson);
        }
    }

    private CommandResult ExecuteApprove(IReadOnlyList<string> args_)
    {
        SpecApproveOptions options;
        try
        {
            options = SpecCommandParser.ParseApprove(args_);
        }
        catch (SpecCliParsingException exception)
        {
            return SpecCommandRenderer.RenderError(exception.Message, exception.IsJson);
        }

        string repoRoot;
        try
        {
            repoRoot = ResolveRepo(options.RepoPath);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Repository path resolution failed: " + exception.Message, options.IsJson);
        }

        try
        {
            SpecLifecycleService service = new(repoRoot, options.SpecId);

            SpecArtifactKind targetKind = options.Artifact == "requirements"
                ? SpecArtifactKind.RequirementSet
                : SpecArtifactKind.WorkSpec;

            SpecArtifactApprovalStatus? preStatus = service.GetApprovalStatus(targetKind);
            SpecApprovalStatus currentPersistedStatus = preStatus?.Status ?? SpecApprovalStatus.NotApproved;

            SpecApprovalLedger? currentLedger = service.LedgerStore.Load();
            SpecStoreOptions ledgerOptions = new()
            {
                Mode = options.Mode,
                ExpectedCurrentRevision = currentLedger?.Revision
            };

            SpecApprovalLedgerStoreResult result = targetKind == SpecArtifactKind.RequirementSet
                ? service.ApproveRequirementSet(
                    options.Revision,
                    ledgerOptions)
                : service.ApproveWorkSpec(
                    options.Revision,
                    ledgerOptions);

            SpecApprovalStatus currentApprovalStatus = options.Mode == SpecWriteMode.Apply
                ? SpecApprovalStatus.Current
                : currentPersistedStatus;
            SpecApprovalStatus proposedApprovalStatus = SpecApprovalStatus.Current;

            return SpecCommandRenderer.RenderApproveResult(
                options.SpecId,
                result,
                currentApprovalStatus,
                proposedApprovalStatus,
                options.IsJson);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(exception, options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Spec approve failed: " + exception.Message, options.IsJson);
        }
    }

    private static string ResolveRepo(string? repoPathRaw_)
    {
        return new RepoPathResolver().Resolve(repoPathRaw_, "spec");
    }
}
