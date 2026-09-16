using System.Reflection;
using AiRepoKit.Agents;
using Xunit;

namespace AiRepoKit.Agents.Abstractions.Tests;

public sealed class AgentProviderIdTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("antigravity")]
    [InlineData("provider-v2")]
    [InlineData("vendor.cli")]
    [InlineData("a1")]
    public void Constructor_AcceptsValidIdentifiers(
        string value_)
    {
        AgentProviderId providerId =
            new(value_);

        Assert.Equal(
            value_,
            providerId.Value);

        Assert.Equal(
            value_,
            providerId.ToString());

        Assert.True(
            AgentProviderId.IsValid(
                value_));

        Assert.True(
            AgentProviderId.TryParse(
                value_,
                out AgentProviderId? parsed));

        Assert.NotNull(
            parsed);

        Assert.Equal(
            value_,
            parsed.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("Codex")]
    [InlineData("ANTIGRAVITY")]
    [InlineData("Provider-V2")]
    [InlineData(".codex")]
    [InlineData("codex.")]
    [InlineData("-codex")]
    [InlineData("codex-")]
    [InlineData("provider_v2")]
    [InlineData("vendor/cli")]
    [InlineData("vendor cli")]
    public void Constructor_RejectsInvalidIdentifiers(
        string value_)
    {
        Assert.False(
            AgentProviderId.IsValid(
                value_));

        Assert.False(
            AgentProviderId.TryParse(
                value_,
                out AgentProviderId? parsed));

        Assert.Null(
            parsed);

        Assert.Throws<ArgumentException>(
            () =>
                new AgentProviderId(
                    value_));
    }

    [Fact]
    public void Constructor_RejectsNull()
    {
        Assert.False(
            AgentProviderId.IsValid(
                null));

        Assert.False(
            AgentProviderId.TryParse(
                null,
                out AgentProviderId? parsed));

        Assert.Null(
            parsed);

        Assert.Throws<ArgumentException>(
            () =>
                new AgentProviderId(
                    null!));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("z")]
    [InlineData("0")]
    [InlineData("9")]
    public void Boundaries_AcceptsSingleValidCharacter(
        string singleChar_)
    {
        AgentProviderId providerId =
            new(singleChar_);

        Assert.Equal(
            singleChar_,
            providerId.Value);

        Assert.True(
            AgentProviderId.IsValid(
                singleChar_));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("-")]
    public void Boundaries_RejectsSingleInvalidSeparator(
        string separator_)
    {
        Assert.False(
            AgentProviderId.IsValid(
                separator_));

        Assert.Throws<ArgumentException>(
            () =>
                new AgentProviderId(
                    separator_));
    }

    [Fact]
    public void Boundaries_AcceptsExactly64Characters()
    {
        string valid64 =
            "a" +
            new string('b', 62) +
            "c";

        Assert.Equal(
            64,
            valid64.Length);

        AgentProviderId providerId =
            new(valid64);

        Assert.Equal(
            valid64,
            providerId.Value);

        Assert.True(
            AgentProviderId.IsValid(
                valid64));
    }

    [Fact]
    public void Boundaries_Rejects65Characters()
    {
        string invalid65 =
            new string('a', 65);

        Assert.False(
            AgentProviderId.IsValid(
                invalid65));

        Assert.Throws<ArgumentException>(
            () =>
                new AgentProviderId(
                    invalid65));
    }

    [Fact]
    public void Constructor_DoesNotNormalizeOrLowercase()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new AgentProviderId(
                    "Vendor.Cli"));

        Assert.Throws<ArgumentException>(
            () =>
                new AgentProviderId(
                    " vendor.cli "));
    }

    [Fact]
    public void ProviderId_HasNoBuiltInProviderConstants()
    {
        FieldInfo[] publicStaticFields =
            typeof(AgentProviderId).GetFields(
                BindingFlags.Public |
                BindingFlags.Static);

        Assert.Empty(
            publicStaticFields);
    }

    [Fact]
    public void ValueSemantics_Equality()
    {
        AgentProviderId a1 =
            new("antigravity");
        AgentProviderId a2 =
            new("antigravity");
        AgentProviderId c1 =
            new("codex");

        Assert.Equal(
            a1,
            a2);
        Assert.True(
            a1 == a2);
        Assert.False(
            a1 != a2);

        Assert.NotEqual(
            a1,
            c1);
        Assert.True(
            a1 != c1);

        Assert.Equal(
            a1.GetHashCode(),
            a2.GetHashCode());
    }

    [Fact]
    public void AgentProviderId_DoesNotImplementIComparable()
    {
        Assert.False(
            typeof(IComparable).IsAssignableFrom(typeof(AgentProviderId)),
            "AgentProviderId must not implement non-generic IComparable.");

        Assert.False(
            typeof(IComparable<AgentProviderId>).IsAssignableFrom(typeof(AgentProviderId)),
            "AgentProviderId must not implement IComparable<AgentProviderId>.");

        MethodInfo? compareTo =
            typeof(AgentProviderId).GetMethod(
                "CompareTo",
                BindingFlags.Public |
                BindingFlags.Instance);

        Assert.Null(
            compareTo);
    }

    [Fact]
    public void AgentProviderId_IsNotValueType_AndCannotMaterializeInvalidDefaultInstance()
    {
        Assert.False(
            typeof(AgentProviderId).IsValueType,
            "AgentProviderId must not be a value type capable of producing an invalid default instance.");

        AgentProviderId? defaultId =
            default;

        Assert.Null(
            defaultId);

        ConstructorInfo? parameterlessCtor =
            typeof(AgentProviderId).GetConstructor(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance,
                Type.EmptyTypes);

        Assert.Null(
            parameterlessCtor);

        Assert.Throws<MissingMethodException>(
            () =>
                Activator.CreateInstance<AgentProviderId>());
    }
}
