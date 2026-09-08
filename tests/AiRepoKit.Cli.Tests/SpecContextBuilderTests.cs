using System.Text.Json;
using AiRepoKit.Cli.Models.SpecContexts;
using AiRepoKit.Cli.Services.ContextBudget;
using AiRepoKit.Cli.Services.SpecContexts;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Context;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class SpecContextBuilderTests
{
    [Fact]
    public void Build_CollectsOnceAndCopiesCanonicalObservedInputsWithoutMutation()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            FakeCollector collector = new(EmptyCollection());
            SpecContextBuilder builder = new(collector, new ContextBudgeter());
            RequirementSet requirementSet = CreateRequirementSet(3);
            WorkSpec workSpec = CreateWorkSpec(5, requirementSetRevision_: 99);
            string requirementSetBefore = JsonSerializer.Serialize(requirementSet);
            string workSpecBefore = JsonSerializer.Serialize(workSpec);
            SpecContextBuildRequest request = new(
                Path.Combine(repoRoot, "."),
                "p03d-test",
                requirementSet,
                workSpec,
                "Orders",
                17,
                1000,
                4321);

            SpecContext result = builder.Build(request);

            Assert.Equal(1, collector.CallCount);
            Assert.NotNull(collector.LastRequest);
            Assert.Equal(Path.GetFullPath(repoRoot), Path.GetFullPath(collector.LastRequest!.RepoRoot));
            Assert.Equal("Orders", collector.LastRequest.Target);
            Assert.Equal(17, collector.LastRequest.Limit);
            Assert.Equal(4321, collector.LastRequest.MaxFiles);
            Assert.Equal("p03d-test", result.SpecId);
            Assert.Equal(new ArtifactRevision(3), result.RequirementSetRevision);
            Assert.Equal(new ArtifactRevision(5), result.WorkSpecRevision);
            Assert.Equal("Orders", result.Target);
            Assert.Equal(requirementSetBefore, JsonSerializer.Serialize(requirementSet));
            Assert.Equal(workSpecBefore, JsonSerializer.Serialize(workSpec));
            Assert.Empty(SpecContextValidator.Validate(result));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Build_OrdersEvidenceAndReferencesUsingFrozenOrdinalRules()
    {
        RepositoryEvidence zEvidence = Evidence("z-evidence");
        RepositoryEvidence aEvidence = Evidence("a-evidence");
        RepositoryEvidence bEvidence = Evidence("b-evidence");
        RepositoryEvidenceCollection collection = new(
            [zEvidence, bEvidence, aEvidence],
            [
                Reference("z-evidence", "file", "src/a.cs", 80),
                Reference("b-evidence", "file", "src/B.cs", 90),
                Reference("a-evidence", "file", "src/A.cs", 90),
                Reference("a-evidence", "endpoint", "src/z.cs#GET", 90)
            ]);

        SpecContext result = CreateBuilder(collection).Build(CreateRequest(referenceLimit: 10, budget: 10000));

        Assert.Equal(new[] { "a-evidence", "b-evidence", "z-evidence" }, result.Evidence.Select(item_ => item_.EvidenceId));
        Assert.Equal(
            new[] { "src/z.cs#GET", "src/A.cs", "src/B.cs", "src/a.cs" },
            result.References.Select(item_ => item_.Reference));
        Assert.False(result.Truncated);
        Assert.Empty(result.Omissions);
    }

    [Fact]
    public void Build_DeduplicatesByOrdinalIdentityAndUsesLowestEvidenceIdForExactTie()
    {
        RepositoryEvidenceCollection collection = new(
            [Evidence("a"), Evidence("b"), Evidence("z")],
            [
                Reference("z", "file", "same.cs", 50),
                Reference("b", "file", "same.cs", 90),
                Reference("a", "file", "same.cs", 90),
                Reference("a", "file", "SAME.cs", 90)
            ]);

        SpecContext result = CreateBuilder(collection).Build(CreateRequest(referenceLimit: 10, budget: 10000));

        Assert.Equal(2, result.References.Count);
        Assert.Contains(result.References, item_ => item_.Reference == "same.cs" && item_.EvidenceId == "a");
        Assert.Contains(result.References, item_ => item_.Reference == "SAME.cs");
        Assert.Empty(result.Omissions);
    }

    [Fact]
    public void Build_EnforcesReferenceLimitWithExplicitOrderedOmissions()
    {
        SpecContextReference first = Reference("evidence", "file", "first.cs", 100);
        SpecContextReference second = Reference("evidence", "file", "second.cs", 90);
        SpecContextReference third = Reference("evidence", "file", "third.cs", 80);
        RepositoryEvidenceCollection collection = new(
            [Evidence("evidence")],
            [third, first, second]);

        SpecContext result = CreateBuilder(collection).Build(CreateRequest(referenceLimit: 1, budget: 10000));

        Assert.Equal(first, Assert.Single(result.References));
        Assert.Equal(new[] { "second.cs", "third.cs" }, result.Omissions.Select(item_ => item_.Reference));
        Assert.All(
            result.Omissions,
            item_ => Assert.Equal("Reference limit exceeded; lower-priority item omitted.", item_.Reason));
        Assert.Equal(ContextBudgeter.EstimateTokens(second), result.Omissions[0].RemovedEstimatedTokens);
        Assert.Equal(ContextBudgeter.EstimateTokens(third), result.Omissions[1].RemovedEstimatedTokens);
        Assert.True(result.Truncated);
        Assert.True(result.References.Count <= result.ReferenceLimit);
    }

    [Fact]
    public void Build_UsesOrdinalPreorderForTightBudgetDespiteBudgeterIgnoreCaseTieBreak()
    {
        SpecContextReference ordinalFirst = Reference("evidence", "file", "B.cs", 50);
        SpecContextReference ignoreCaseFirst = Reference("evidence", "file", "a.cs", 50);
        int budget = ContextBudgeter.EstimateTokens(ordinalFirst);
        Assert.Equal(budget, ContextBudgeter.EstimateTokens(ignoreCaseFirst));
        RepositoryEvidenceCollection collection = new(
            [Evidence("evidence")],
            [ignoreCaseFirst, ordinalFirst]);

        SpecContext result = CreateBuilder(collection).Build(CreateRequest(referenceLimit: 2, budget: budget));

        Assert.Equal("B.cs", Assert.Single(result.References).Reference);
        SpecContextOmission omission = Assert.Single(result.Omissions);
        Assert.Equal("a.cs", omission.Reference);
        Assert.Equal("Budget exceeded; lower-priority item omitted.", omission.Reason);
        Assert.Equal(ContextBudgeter.EstimateTokens(ignoreCaseFirst), omission.RemovedEstimatedTokens);
        Assert.True(result.EstimatedTokens <= result.Budget);
        Assert.True(result.References.Count <= result.ReferenceLimit);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Build_NoOmissionsMeansNotTruncatedAndRepeatedBuildsAreIdentical()
    {
        RepositoryEvidenceCollection collection = new(
            [Evidence("evidence")],
            [Reference("evidence", "file", "src/File.cs", 50)]);
        SpecContextBuilder builder = CreateBuilder(collection);
        SpecContextBuildRequest request = CreateRequest(referenceLimit: 10, budget: 10000);

        string first = JsonSerializer.Serialize(builder.Build(request));
        SpecContext secondContext = builder.Build(request);
        string second = JsonSerializer.Serialize(secondContext);

        Assert.Equal(first, second);
        Assert.False(secondContext.Truncated);
        Assert.Empty(secondContext.Omissions);
        Assert.Empty(SpecContextValidator.Validate(secondContext));
    }

    [Theory]
    [InlineData("INVALID", 10, 100, 1000)]
    [InlineData("valid-spec", 0, 100, 1000)]
    [InlineData("valid-spec", 101, 100, 1000)]
    [InlineData("valid-spec", 10, 0, 1000)]
    [InlineData("valid-spec", 10, -1, 1000)]
    [InlineData("valid-spec", 10, 100, 0)]
    [InlineData("valid-spec", 10, 100, -1)]
    public void Build_InvalidInputsAreRejectedBeforeCollection(
        string specId,
        int referenceLimit,
        int budget,
        int maxFiles)
    {
        FakeCollector collector = new(EmptyCollection());
        SpecContextBuilder builder = new(collector, new ContextBudgeter());
        SpecContextBuildRequest request = CreateRequest(
            specId: specId,
            referenceLimit: referenceLimit,
            budget: budget,
            maxFiles: maxFiles);

        Assert.ThrowsAny<ArgumentException>(() => builder.Build(request));
        Assert.Equal(0, collector.CallCount);
    }

    [Fact]
    public void Build_DoesNotCreateSpecContextPersistenceDirectory()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextBuildRequest request = CreateRequest(repoRoot: repoRoot);

            CreateBuilder(EmptyCollection()).Build(request);

            Assert.False(Directory.Exists(Path.Combine(repoRoot, ".ai", "generated", "spec-context")));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    private static SpecContextBuilder CreateBuilder(
        RepositoryEvidenceCollection collection_)
    {
        return new SpecContextBuilder(new FakeCollector(collection_), new ContextBudgeter());
    }

    private static SpecContextBuildRequest CreateRequest(
        string? repoRoot = null,
        string specId = "valid-spec",
        int referenceLimit = 10,
        int budget = 1000,
        int maxFiles = 1000)
    {
        return new SpecContextBuildRequest(
            repoRoot ?? Path.GetTempPath(),
            specId,
            CreateRequirementSet(2),
            CreateWorkSpec(4, 77),
            "target",
            referenceLimit,
            budget,
            maxFiles);
    }

    private static RequirementSet CreateRequirementSet(
        int revision_)
    {
        return new RequirementSet
        {
            Revision = new ArtifactRevision(revision_),
            Inputs = [],
            Requirements = []
        };
    }

    private static WorkSpec CreateWorkSpec(
        int revision_,
        int requirementSetRevision_)
    {
        return new WorkSpec
        {
            Revision = new ArtifactRevision(revision_),
            RequirementSetRevision = new ArtifactRevision(requirementSetRevision_),
            Constraints = [],
            AcceptanceCriteria = []
        };
    }

    private static RepositoryEvidenceCollection EmptyCollection()
    {
        return new RepositoryEvidenceCollection([], []);
    }

    private static RepositoryEvidence Evidence(
        string evidenceId_)
    {
        return new RepositoryEvidence
        {
            EvidenceId = evidenceId_,
            Source = "test",
            Kind = "test",
            Reference = "test:" + evidenceId_,
            Availability = RepositoryEvidenceAvailability.Available,
            Freshness = RepositoryEvidenceFreshness.Unknown
        };
    }

    private static SpecContextReference Reference(
        string evidenceId_,
        string kind_,
        string reference_,
        int priority_)
    {
        return new SpecContextReference
        {
            EvidenceId = evidenceId_,
            Kind = kind_,
            Reference = reference_,
            Reason = "Test reference.",
            Priority = priority_
        };
    }

    private static string CreateTempRepo()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "airepo_spec_context_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempRepo(
        string path_)
    {
        if (Directory.Exists(path_))
        {
            try
            {
                Directory.Delete(path_, true);
            }
            catch
            {
            }
        }
    }

    private sealed class FakeCollector :
        IRepositoryEvidenceCollector
    {
        private readonly RepositoryEvidenceCollection _collection;

        public FakeCollector(
            RepositoryEvidenceCollection collection_)
        {
            this._collection = collection_;
        }

        public int CallCount
        {
            get;
            private set;
        }

        public RepositoryEvidenceCollectionRequest? LastRequest
        {
            get;
            private set;
        }

        public RepositoryEvidenceCollection Collect(
            RepositoryEvidenceCollectionRequest request_)
        {
            this.CallCount++;
            this.LastRequest = request_;
            return this._collection;
        }
    }
}
