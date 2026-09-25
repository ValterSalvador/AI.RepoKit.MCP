namespace AiRepoKit.Execution.Tests;

using AiRepoKit.Execution;
using Xunit;

public sealed class ContextCompilerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ContextCandidate_RejectsInvalidId(
        string? id_)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new ContextCandidate(
                    id_!,
                    "content",
                    0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ContextCandidate_RejectsNullOrEmptyContent(
        string? content_)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new ContextCandidate(
                    "candidate",
                    content_!,
                    0));
    }

    [Fact]
    public void ContextCandidate_PreservesExactValuesIncludingWhitespaceContent()
    {
        ContextCandidate candidate =
            new(
                "  Candidate-A  ",
                "   ",
                7);

        Assert.Equal(
            "  Candidate-A  ",
            candidate.Id);

        Assert.Equal(
            "   ",
            candidate.Content);

        Assert.Equal(
            7,
            candidate.RelevanceScore);
    }

    [Fact]
    public void ContextCandidate_RejectsNegativeRelevance()
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    new ContextCandidate(
                        "candidate",
                        "content",
                        -1));

        Assert.Equal(
            "relevanceScore_",
            exception.ParamName);
    }

    [Fact]
    public void CompiledContextItem_PreservesExactValues()
    {
        CompiledContextItem item =
            new(
                " candidate ",
                "   ",
                2,
                1);

        Assert.Equal(
            " candidate ",
            item.Id);

        Assert.Equal(
            "   ",
            item.Content);

        Assert.Equal(
            2,
            item.RelevanceScore);

        Assert.Equal(
            1,
            item.EstimatedTokens);
    }

    [Fact]
    public void CompiledContextItem_RejectsNonPositiveEstimatedTokens()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new CompiledContextItem(
                    "candidate",
                    "content",
                    0,
                    0));
    }

    [Fact]
    public void CompiledContext_RejectsNonPositiveTokenBudget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new CompiledContext(
                    0,
                    1,
                    0,
                    false,
                    Array.Empty<CompiledContextItem>(),
                    Array.Empty<string>()));
    }

    [Fact]
    public void CompiledContext_RejectsNonPositiveItemLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new CompiledContext(
                    1,
                    0,
                    0,
                    false,
                    Array.Empty<CompiledContextItem>(),
                    Array.Empty<string>()));
    }

    [Fact]
    public void CompiledContext_RejectsNegativeEstimatedTokens()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new CompiledContext(
                    1,
                    1,
                    -1,
                    false,
                    Array.Empty<CompiledContextItem>(),
                    Array.Empty<string>()));
    }

    [Fact]
    public void CompiledContext_RejectsEstimateAboveBudget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new CompiledContext(
                    1,
                    1,
                    2,
                    false,
                    Array.Empty<CompiledContextItem>(),
                    Array.Empty<string>()));
    }

    [Fact]
    public void CompiledContext_SnapshotsCollectionsAndExposesReadOnlyViews()
    {
        CompiledContextItem[] items =
        [
            Item("a")
        ];

        string[] omitted =
        [
            "b"
        ];

        CompiledContext context =
            new(
                10,
                1,
                1,
                true,
                items,
                omitted);

        items[0] =
            Item("replacement");

        omitted[0] =
            "replacement";

        Assert.Equal(
            "a",
            context.Items[0].Id);

        Assert.Equal(
            "b",
            context.OmittedCandidateIds[0]);

        IList<CompiledContextItem> mutableItems =
            Assert.IsAssignableFrom<IList<CompiledContextItem>>(
                context.Items);

        IList<string> mutableOmitted =
            Assert.IsAssignableFrom<IList<string>>(
                context.OmittedCandidateIds);

        Assert.True(
            mutableItems.IsReadOnly);

        Assert.True(
            mutableOmitted.IsReadOnly);

        Assert.Throws<NotSupportedException>(
            () =>
                mutableItems.Clear());

        Assert.Throws<NotSupportedException>(
            () =>
                mutableOmitted.Clear());
    }

    [Fact]
    public void CompiledContext_RejectsNullItem()
    {
        CompiledContextItem[] items =
        [
            null!
        ];

        Assert.Throws<ArgumentException>(
            () =>
                new CompiledContext(
                    10,
                    1,
                    0,
                    false,
                    items,
                    Array.Empty<string>()));
    }

    [Fact]
    public void CompiledContext_RejectsBlankOmittedId()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new CompiledContext(
                    10,
                    1,
                    0,
                    true,
                    Array.Empty<CompiledContextItem>(),
                    [" "]));
    }

    [Fact]
    public void CompiledContext_RejectsDuplicateSelectedIdsOrdinal()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new CompiledContext(
                    10,
                    2,
                    2,
                    false,
                    [
                        Item("a"),
                        Item("a")
                    ],
                    Array.Empty<string>()));
    }

    [Fact]
    public void CompiledContext_RejectsDuplicateOmittedIdsOrdinal()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new CompiledContext(
                    10,
                    1,
                    0,
                    true,
                    Array.Empty<CompiledContextItem>(),
                    [
                        "a",
                        "a"
                    ]));
    }

    [Fact]
    public void CompiledContext_RejectsSelectedOmittedOverlap()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new CompiledContext(
                    10,
                    1,
                    1,
                    true,
                    [
                        Item("a")
                    ],
                    [
                        "a"
                    ]));
    }

    [Fact]
    public void CompiledContext_RejectsIncorrectTokenSum()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new CompiledContext(
                    10,
                    1,
                    2,
                    false,
                    [
                        Item("a")
                    ],
                    Array.Empty<string>()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompiledContext_RejectsTruncationMismatch(
        bool truncated_)
    {
        IReadOnlyList<string> omitted =
            truncated_
                ? Array.Empty<string>()
                : ["omitted"];

        Assert.Throws<ArgumentException>(
            () =>
                new CompiledContext(
                    10,
                    1,
                    0,
                    truncated_,
                    Array.Empty<CompiledContextItem>(),
                    omitted));
    }

    [Fact]
    public void Compile_RejectsNullCandidates()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                ContextCompiler.Compile(
                    null!,
                    1,
                    1));
    }

    [Fact]
    public void Compile_RejectsNullCandidate()
    {
        ContextCandidate[] candidates =
        [
            null!
        ];

        Assert.Throws<ArgumentException>(
            () =>
                ContextCompiler.Compile(
                    candidates,
                    1,
                    1));
    }

    [Fact]
    public void Compile_RejectsNonPositiveTokenBudget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                ContextCompiler.Compile(
                    Array.Empty<ContextCandidate>(),
                    0,
                    1));
    }

    [Fact]
    public void Compile_RejectsNonPositiveItemLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                ContextCompiler.Compile(
                    Array.Empty<ContextCandidate>(),
                    1,
                    0));
    }

    [Fact]
    public void Compile_RejectsDuplicateIdsUsingOrdinalIdentity()
    {
        Assert.Throws<ArgumentException>(
            () =>
                ContextCompiler.Compile(
                    [
                        Candidate("a", 1),
                        Candidate("a", 2)
                    ],
                    10,
                    10));
    }

    [Fact]
    public void Compile_TreatsDifferentCaseIdsAsDistinct()
    {
        CompiledContext context =
            ContextCompiler.Compile(
                [
                    Candidate("candidate-a", 1),
                    Candidate("CANDIDATE-A", 1)
                ],
                10,
                10);

        Assert.Equal(
            [
                "CANDIDATE-A",
                "candidate-a"
            ],
            context.Items
                .Select(item_ => item_.Id)
                .ToArray());
    }

    [Fact]
    public void Compile_UsesRelevanceDescendingThenOrdinalId()
    {
        CompiledContext context =
            ContextCompiler.Compile(
                [
                    Candidate("c", 1),
                    Candidate("b", 5),
                    Candidate("a", 5)
                ],
                10,
                10);

        Assert.Equal(
            [
                "a",
                "b",
                "c"
            ],
            context.Items
                .Select(item_ => item_.Id)
                .ToArray());
    }

    [Fact]
    public void Compile_InputOrderDoesNotAffectResult()
    {
        ContextCandidate a =
            Candidate("a", 5);

        ContextCandidate b =
            Candidate("b", 5);

        ContextCandidate c =
            Candidate("c", 1);

        CompiledContext first =
            ContextCompiler.Compile(
                [
                    c,
                    b,
                    a
                ],
                10,
                10);

        CompiledContext second =
            ContextCompiler.Compile(
                [
                    a,
                    c,
                    b
                ],
                10,
                10);

        Assert.Equal(
            first.Items.Select(item_ => item_.Id),
            second.Items.Select(item_ => item_.Id));

        Assert.Equal(
            first.OmittedCandidateIds,
            second.OmittedCandidateIds);
    }

    [Fact]
    public void Compile_ReusesDeterministicWorkTokenEstimatorExactly()
    {
        ContextCandidate candidate =
            new(
                "a",
                "abcde",
                1);

        CompiledContext context =
            ContextCompiler.Compile(
                [candidate],
                10,
                10);

        CompiledContextItem item =
            Assert.Single(
                context.Items);

        Assert.Equal(
            DeterministicWorkEstimator.EstimateTokens(
                candidate.Content),
            item.EstimatedTokens);

        Assert.Equal(
            2,
            item.EstimatedTokens);

        Assert.Equal(
            2,
            context.EstimatedTokens);
    }

    [Fact]
    public void Compile_EnforcesItemLimit()
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

        Assert.Equal(
            [
                "a",
                "b"
            ],
            context.Items
                .Select(item_ => item_.Id)
                .ToArray());

        Assert.Equal(
            ["c"],
            context.OmittedCandidateIds);

        Assert.True(
            context.Truncated);
    }

    [Fact]
    public void Compile_EnforcesTokenBudget()
    {
        CompiledContext context =
            ContextCompiler.Compile(
                [
                    new ContextCandidate(
                        "a",
                        "abcdefgh",
                        2),
                    new ContextCandidate(
                        "b",
                        "abcdefgh",
                        1)
                ],
                2,
                10);

        CompiledContextItem item =
            Assert.Single(
                context.Items);

        Assert.Equal(
            "a",
            item.Id);

        Assert.Equal(
            ["b"],
            context.OmittedCandidateIds);

        Assert.Equal(
            2,
            context.EstimatedTokens);
    }

    [Fact]
    public void Compile_SkipsOversizedHigherRankedCandidateAndContinues()
    {
        CompiledContext context =
            ContextCompiler.Compile(
                [
                    new ContextCandidate(
                        "large",
                        new string('x', 12),
                        100),
                    new ContextCandidate(
                        "small",
                        "abcd",
                        1)
                ],
                1,
                10);

        CompiledContextItem item =
            Assert.Single(
                context.Items);

        Assert.Equal(
            "small",
            item.Id);

        Assert.Equal(
            ["large"],
            context.OmittedCandidateIds);
    }

    [Fact]
    public void Compile_EmptyInputProducesEmptyUntruncatedContext()
    {
        CompiledContext context =
            ContextCompiler.Compile(
                Array.Empty<ContextCandidate>(),
                1,
                1);

        Assert.Empty(
            context.Items);

        Assert.Empty(
            context.OmittedCandidateIds);

        Assert.Equal(
            0,
            context.EstimatedTokens);

        Assert.False(
            context.Truncated);
    }

    [Fact]
    public void Compile_WhenNothingFitsProducesEmptyTruncatedContext()
    {
        CompiledContext context =
            ContextCompiler.Compile(
                [
                    new ContextCandidate(
                        "large",
                        "abcdefgh",
                        1)
                ],
                1,
                10);

        Assert.Empty(
            context.Items);

        Assert.Equal(
            ["large"],
            context.OmittedCandidateIds);

        Assert.True(
            context.Truncated);
    }

    [Fact]
    public void Compile_OutputCollectionsAreReadOnly()
    {
        CompiledContext context =
            ContextCompiler.Compile(
                [
                    Candidate("a", 2),
                    Candidate("b", 1)
                ],
                1,
                10);

        IList<CompiledContextItem> items =
            Assert.IsAssignableFrom<IList<CompiledContextItem>>(
                context.Items);

        IList<string> omitted =
            Assert.IsAssignableFrom<IList<string>>(
                context.OmittedCandidateIds);

        Assert.True(
            items.IsReadOnly);

        Assert.True(
            omitted.IsReadOnly);
    }

    [Fact]
    public void Compile_RepeatedCallsAreDeterministic()
    {
        ContextCandidate[] candidates =
        [
            Candidate("c", 1),
            Candidate("b", 4),
            Candidate("a", 4)
        ];

        CompiledContext first =
            ContextCompiler.Compile(
                candidates,
                2,
                2);

        CompiledContext second =
            ContextCompiler.Compile(
                candidates,
                2,
                2);

        Assert.Equal(
            first.Items.Select(item_ => item_.Id),
            second.Items.Select(item_ => item_.Id));

        Assert.Equal(
            first.Items.Select(item_ => item_.EstimatedTokens),
            second.Items.Select(item_ => item_.EstimatedTokens));

        Assert.Equal(
            first.OmittedCandidateIds,
            second.OmittedCandidateIds);

        Assert.Equal(
            first.EstimatedTokens,
            second.EstimatedTokens);

        Assert.Equal(
            first.Truncated,
            second.Truncated);
    }

    [Fact]
    public void Compile_DoesNotMutateInput()
    {
        ContextCandidate first =
            Candidate("b", 1);

        ContextCandidate second =
            Candidate("a", 2);

        ContextCandidate[] candidates =
        [
            first,
            second
        ];

        _ =
            ContextCompiler.Compile(
                candidates,
                10,
                10);

        Assert.Same(
            first,
            candidates[0]);

        Assert.Same(
            second,
            candidates[1]);
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

    private static CompiledContextItem Item(
        string id_)
    {
        return new CompiledContextItem(
            id_,
            "abcd",
            0,
            1);
    }
}
