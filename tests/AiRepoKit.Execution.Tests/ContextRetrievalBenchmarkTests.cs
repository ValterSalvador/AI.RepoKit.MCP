namespace AiRepoKit.Execution.Tests;

using AiRepoKit.Execution;
using Xunit;

public sealed class ContextRetrievalBenchmarkTests
{
    [Fact]
    public void PerfectRetrieval_HasPerfectMetrics()
    {
        CompiledContext context =
            ContextCompiler.Compile(
                [
                    Candidate("a", 3),
                    Candidate("b", 2),
                    Candidate("c", 1)
                ],
                10,
                2);

        var metrics =
            Evaluate(
                context,
                [
                    "a",
                    "b"
                ]);

        Assert.Equal(1.0, metrics.Precision);
        Assert.Equal(1.0, metrics.Recall);
        Assert.Equal(1.0, metrics.F1);
    }

    [Fact]
    public void PartialRetrieval_HasDeterministicPartialMetrics()
    {
        CompiledContext context =
            ContextCompiler.Compile(
                [
                    Candidate("a", 3),
                    Candidate("c", 2),
                    Candidate("b", 1)
                ],
                10,
                2);

        var metrics =
            Evaluate(
                context,
                [
                    "a",
                    "b"
                ]);

        Assert.Equal(0.5, metrics.Precision);
        Assert.Equal(0.5, metrics.Recall);
        Assert.Equal(0.5, metrics.F1);
    }

    [Fact]
    public void ZeroRelevantRetrieval_HasZeroMetrics()
    {
        CompiledContext context =
            ContextCompiler.Compile(
                [
                    Candidate("c", 4),
                    Candidate("d", 3),
                    Candidate("a", 2),
                    Candidate("b", 1)
                ],
                10,
                2);

        var metrics =
            Evaluate(
                context,
                [
                    "a",
                    "b"
                ]);

        Assert.Equal(0.0, metrics.Precision);
        Assert.Equal(0.0, metrics.Recall);
        Assert.Equal(0.0, metrics.F1);
    }

    [Fact]
    public void BudgetInducedOmission_IsReflectedInMetrics()
    {
        CompiledContext context =
            ContextCompiler.Compile(
                [
                    new ContextCandidate(
                        "a",
                        "abcdefgh",
                        2),
                    Candidate("b", 1)
                ],
                1,
                10);

        var metrics =
            Evaluate(
                context,
                [
                    "a",
                    "b"
                ]);

        Assert.Equal(
            ["b"],
            context.Items
                .Select(item_ => item_.Id)
                .ToArray());

        Assert.Equal(1.0, metrics.Precision);
        Assert.Equal(0.5, metrics.Recall);
        Assert.Equal(2.0 / 3.0, metrics.F1, 12);
    }

    [Fact]
    public void ItemLimitInducedOmission_IsReflectedInMetrics()
    {
        CompiledContext context =
            ContextCompiler.Compile(
                [
                    Candidate("a", 2),
                    Candidate("b", 1)
                ],
                10,
                1);

        var metrics =
            Evaluate(
                context,
                [
                    "a",
                    "b"
                ]);

        Assert.Equal(
            ["a"],
            context.Items
                .Select(item_ => item_.Id)
                .ToArray());

        Assert.Equal(1.0, metrics.Precision);
        Assert.Equal(0.5, metrics.Recall);
        Assert.Equal(2.0 / 3.0, metrics.F1, 12);
    }

    [Fact]
    public void OrdinalTieBreaking_IsDeterministic()
    {
        CompiledContext context =
            ContextCompiler.Compile(
                [
                    Candidate("b", 1),
                    Candidate("a", 1)
                ],
                10,
                1);

        var metrics =
            Evaluate(
                context,
                ["a"]);

        Assert.Equal(
            ["a"],
            context.Items
                .Select(item_ => item_.Id)
                .ToArray());

        Assert.Equal(1.0, metrics.Precision);
        Assert.Equal(1.0, metrics.Recall);
        Assert.Equal(1.0, metrics.F1);
    }

    [Fact]
    public void OversizedHigherRankedCandidate_DoesNotBlockLaterFit()
    {
        CompiledContext context =
            ContextCompiler.Compile(
                [
                    new ContextCandidate(
                        "large",
                        new string('x', 12),
                        100),
                    Candidate("small", 1)
                ],
                1,
                10);

        var metrics =
            Evaluate(
                context,
                ["small"]);

        Assert.Equal(
            ["small"],
            context.Items
                .Select(item_ => item_.Id)
                .ToArray());

        Assert.Equal(
            ["large"],
            context.OmittedCandidateIds);

        Assert.Equal(1.0, metrics.Precision);
        Assert.Equal(1.0, metrics.Recall);
        Assert.Equal(1.0, metrics.F1);
    }

    [Fact]
    public void InputPermutation_ProducesIdenticalRetrievalAndMetrics()
    {
        ContextCandidate a =
            Candidate("a", 4);

        ContextCandidate b =
            Candidate("b", 4);

        ContextCandidate c =
            Candidate("c", 1);

        CompiledContext first =
            ContextCompiler.Compile(
                [
                    c,
                    b,
                    a
                ],
                2,
                2);

        CompiledContext second =
            ContextCompiler.Compile(
                [
                    a,
                    c,
                    b
                ],
                2,
                2);

        string[] firstIds =
            first.Items
                .Select(item_ => item_.Id)
                .ToArray();

        string[] secondIds =
            second.Items
                .Select(item_ => item_.Id)
                .ToArray();

        Assert.Equal(
            firstIds,
            secondIds);

        var firstMetrics =
            Evaluate(
                first,
                [
                    "a",
                    "c"
                ]);

        var secondMetrics =
            Evaluate(
                second,
                [
                    "a",
                    "c"
                ]);

        Assert.Equal(
            firstMetrics,
            secondMetrics);
    }

    private static (
        double Precision,
        double Recall,
        double F1) Evaluate(
        CompiledContext context_,
        IReadOnlyList<string> expectedRelevantIds_)
    {
        ArgumentNullException.ThrowIfNull(
            context_);

        ArgumentNullException.ThrowIfNull(
            expectedRelevantIds_);

        if (expectedRelevantIds_.Count == 0)
        {
            throw new ArgumentException(
                "Expected relevant ids must not be empty.",
                nameof(expectedRelevantIds_));
        }

        HashSet<string> expected =
            new(StringComparer.Ordinal);

        foreach (string? id in expectedRelevantIds_)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "Expected relevant ids must not contain blanks.",
                    nameof(expectedRelevantIds_));
            }

            if (!expected.Add(id))
            {
                throw new ArgumentException(
                    $"Duplicate expected relevant id '{id}'.",
                    nameof(expectedRelevantIds_));
            }
        }

        HashSet<string> retrieved =
            new(
                context_
                    .Items
                    .Select(item_ => item_.Id),
                StringComparer.Ordinal);

        int relevantRetrieved =
            retrieved.Count(
                id_ =>
                    expected.Contains(id_));

        double precision =
            retrieved.Count == 0
                ? 0.0
                : (double) relevantRetrieved /
                    retrieved.Count;

        double recall =
            (double) relevantRetrieved /
            expected.Count;

        double f1 =
            precision + recall == 0.0
                ? 0.0
                : 2.0 *
                    precision *
                    recall /
                    (precision + recall);

        return (
            precision,
            recall,
            f1);
    }

    private static ContextCandidate Candidate(
        string id_,
        int relevanceScore_)
    {
        return new ContextCandidate(
            id_,
            "abcd",
            relevanceScore_);
    }
}
