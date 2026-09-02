using AiRepoKit.Spec;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecPersistenceTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("ordinary-hyphenated-id")]
    public void SpecId_AcceptsCanonicalForms(
        string value_)
    {
        Assert.True(
            SpecId.TryParse(
                value_,
                out SpecId specId));
        Assert.Equal(
            value_,
            specId.Value);
    }

    [Fact]
    public void SpecId_AcceptsSixtyFourCharacterForm()
    {
        string value =
            new(
                'a',
                64);

        Assert.True(
            SpecId.TryParse(
                value,
                out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("Uppercase")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("white space")]
    [InlineData("dot.id")]
    [InlineData("slash/id")]
    [InlineData("backslash\\id")]
    [InlineData("colon:id")]
    [InlineData("unicodé")]
    public void SpecId_RejectsNonCanonicalForms(
        string? value_)
    {
        Assert.False(
            SpecId.TryParse(
                value_,
                out _));
    }

    [Theory]
    [InlineData("con")]
    [InlineData("prn")]
    [InlineData("aux")]
    [InlineData("nul")]
    [InlineData("com1")]
    [InlineData("com2")]
    [InlineData("com3")]
    [InlineData("com4")]
    [InlineData("com5")]
    [InlineData("com6")]
    [InlineData("com7")]
    [InlineData("com8")]
    [InlineData("com9")]
    [InlineData("lpt1")]
    [InlineData("lpt2")]
    [InlineData("lpt3")]
    [InlineData("lpt4")]
    [InlineData("lpt5")]
    [InlineData("lpt6")]
    [InlineData("lpt7")]
    [InlineData("lpt8")]
    [InlineData("lpt9")]
    public void SpecId_RejectsWindowsDeviceNames(
        string value_)
    {
        Assert.False(
            SpecId.TryParse(
                value_,
                out _));
    }

    [Fact]
    public void SpecId_ToStringComparisonAndEqualityAreOrdinalAndDeterministic()
    {
        SpecId[] specIds =
        [
            new("spec-b"),
            new("spec-a")
        ];

        Array.Sort(
            specIds);

        Assert.Equal(
            ["spec-a", "spec-b"],
            specIds
                .Select(
                    specId =>
                        specId.ToString())
                .ToArray());
        Assert.Equal(
            new SpecId(
                "spec-a"),
            specIds[0]);
    }

    [Fact]
    public void ArtifactPaths_ReturnOnlyCanonicalLayoutFromNormalizedRoot()
    {
        string repositoryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "parent",
                "..",
                "repository");
        SpecArtifactPaths paths =
            new(
                repositoryRoot,
                new SpecId(
                    "spec-1"));
        string normalizedRoot =
            Path.GetFullPath(
                repositoryRoot);
        string specDirectory =
            Path.Combine(
                normalizedRoot,
                ".ai",
                "specs",
                "spec-1");

        Assert.Equal(
            normalizedRoot,
            paths.RepositoryRoot);
        Assert.Equal(
            specDirectory,
            paths.SpecDirectory);
        Assert.Equal(
            Path.Combine(
                specDirectory,
                "requirements.json"),
            paths.GetArtifactPath(
                SpecArtifactKind.RequirementSet));
        Assert.Equal(
            Path.Combine(
                specDirectory,
                "work-spec.json"),
            paths.GetArtifactPath(
                SpecArtifactKind.WorkSpec));
        Assert.Equal(
            Path.Combine(
                specDirectory,
                "implementation-plan.json"),
            paths.GetArtifactPath(
                SpecArtifactKind.ImplementationPlan));
    }

    [Fact]
    public void ArtifactPaths_RejectUnsupportedArtifactKind()
    {
        SpecArtifactPaths paths =
            new(
                Path.GetTempPath(),
                new SpecId(
                    "spec-1"));

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                paths.GetArtifactPath(
                    (SpecArtifactKind)999));
    }

    [Fact]
    public void ArtifactPaths_StayWithinRootWhoseSiblingSharesItsPrefix()
    {
        string parent =
            Path.Combine(
                Path.GetTempPath(),
                "airepokit-spec-paths-" + Guid.NewGuid().ToString("N"));
        string repositoryRoot =
            Path.Combine(
                parent,
                "repo");
        string sibling =
            Path.Combine(
                parent,
                "repo-sibling");
        SpecArtifactPaths paths =
            new(
                repositoryRoot,
                new SpecId(
                    "spec-1"));

        Assert.StartsWith(
            repositoryRoot + Path.DirectorySeparatorChar,
            paths.SpecDirectory,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
        Assert.DoesNotContain(
            sibling,
            paths.SpecDirectory,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactPaths_CreateNoDirectoryOrFile()
    {
        string repositoryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "airepokit-spec-no-create-" + Guid.NewGuid().ToString("N"));

        SpecArtifactPaths paths =
            new(
                repositoryRoot,
                new SpecId(
                    "spec-1"));
        string artifactPath =
            paths.GetArtifactPath(
                SpecArtifactKind.RequirementSet);

        Assert.False(
            Directory.Exists(
                repositoryRoot));
        Assert.False(
            File.Exists(
                artifactPath));
    }

    [Fact]
    public void ArtifactPaths_RejectExistingSymbolicLinkComponentWhereSupported()
    {
        string repositoryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "airepokit-spec-links-" + Guid.NewGuid().ToString("N"));
        string target =
            Path.Combine(
                Path.GetTempPath(),
                "airepokit-spec-link-target-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(
                repositoryRoot);
            Directory.CreateDirectory(
                target);

            try
            {
                Directory.CreateSymbolicLink(
                    Path.Combine(
                        repositoryRoot,
                        ".ai"),
                    target);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                PlatformNotSupportedException)
            {
                return;
            }

            Assert.Throws<InvalidOperationException>(
                () =>
                    new SpecArtifactPaths(
                        repositoryRoot,
                        new SpecId(
                            "spec-1")));
        }
        finally
        {
            try
            {
                Directory.Delete(
                    Path.Combine(
                        repositoryRoot,
                        ".ai"));
            }
            catch
            {
            }

            try
            {
                Directory.Delete(
                    repositoryRoot,
                    true);
            }
            catch
            {
            }

            try
            {
                Directory.Delete(
                    target,
                    true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void ArtifactPaths_RejectExistingArtifactSymbolicLinkWhereSupported()
    {
        string repositoryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "airepokit-spec-artifact-link-" + Guid.NewGuid().ToString("N"));
        string artifactDirectory =
            Path.Combine(
                repositoryRoot,
                ".ai",
                "specs",
                "spec-1");
        string link =
            Path.Combine(
                artifactDirectory,
                "requirements.json");
        string target =
            Path.Combine(
                repositoryRoot,
                "target.json");

        try
        {
            Directory.CreateDirectory(
                artifactDirectory);
            File.WriteAllText(
                target,
                "{}");

            try
            {
                File.CreateSymbolicLink(
                    link,
                    target);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                PlatformNotSupportedException)
            {
                return;
            }

            SpecArtifactPaths paths =
                new(
                    repositoryRoot,
                    new SpecId(
                        "spec-1"));

            Assert.Throws<InvalidOperationException>(
                () =>
                    paths.GetArtifactPath(
                        SpecArtifactKind.RequirementSet));
        }
        finally
        {
            try
            {
                File.Delete(
                    link);
            }
            catch
            {
            }

            try
            {
                Directory.Delete(
                    repositoryRoot,
                    true);
            }
            catch
            {
            }
        }
    }
}
