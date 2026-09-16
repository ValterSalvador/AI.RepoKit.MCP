using System.Reflection;
using AiRepoKit.Agents;
using Xunit;

namespace AiRepoKit.Agents.Abstractions.Tests;

public sealed class AgentCapabilityTests
{
    [Fact]
    public void AgentCapability_IsNotValueType_AndCannotMaterializeInvalidDefaultInstance()
    {
        Assert.False(
            typeof(AgentCapability).IsValueType,
            "AgentCapability must not be a value type capable of producing an invalid default instance.");

        AgentCapability? defaultCapability =
            default;

        Assert.Null(
            defaultCapability);

        ConstructorInfo? parameterlessCtor =
            typeof(AgentCapability).GetConstructor(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance,
                Type.EmptyTypes);

        Assert.Null(
            parameterlessCtor);

        Assert.Throws<MissingMethodException>(
            () =>
                Activator.CreateInstance<AgentCapability>());
    }

    [Theory]
    [InlineData("text")]
    [InlineData("tools")]
    [InlineData("session.resume")]
    [InlineData("structured-output")]
    [InlineData("a1")]
    [InlineData("a.b-c.1-2")]
    public void Constructor_AcceptsValidSyntax(
        string value_)
    {
        AgentCapability capability =
            new(value_);

        Assert.Equal(
            value_,
            capability.Value);

        Assert.Equal(
            value_,
            capability.ToString());
    }

    [Fact]
    public void Constructor_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                new AgentCapability(
                    null!));
    }

    [Fact]
    public void Constructor_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new AgentCapability(
                    string.Empty));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData(" tools")]
    [InlineData("tools ")]
    [InlineData(" tools ")]
    public void Constructor_RejectsWhitespace(
        string value_)
    {
        Assert.Throws<ArgumentException>(
            () =>
                new AgentCapability(
                    value_));
    }

    [Theory]
    [InlineData("Text")]
    [InlineData("TOOLS")]
    [InlineData("Structured-Output")]
    [InlineData("Session.Resume")]
    public void Constructor_RejectsUppercase(
        string value_)
    {
        Assert.Throws<ArgumentException>(
            () =>
                new AgentCapability(
                    value_));
    }

    [Theory]
    [InlineData("tool_s")]
    [InlineData("tool/s")]
    [InlineData("tool:s")]
    [InlineData("tool@s")]
    [InlineData("tool#s")]
    [InlineData("tool$s")]
    [InlineData("tool%s")]
    [InlineData("tool^s")]
    [InlineData("tool&s")]
    [InlineData("tool*s")]
    [InlineData("tool(s)")]
    public void Constructor_RejectsInvalidPunctuation(
        string value_)
    {
        Assert.Throws<ArgumentException>(
            () =>
                new AgentCapability(
                    value_));
    }

    [Theory]
    [InlineData(".tools")]
    [InlineData("-tools")]
    [InlineData(".a")]
    [InlineData("-a")]
    public void Constructor_RejectsLeadingSeparator(
        string value_)
    {
        Assert.Throws<ArgumentException>(
            () =>
                new AgentCapability(
                    value_));
    }

    [Theory]
    [InlineData("tools.")]
    [InlineData("tools-")]
    [InlineData("a.")]
    [InlineData("a-")]
    public void Constructor_RejectsTrailingSeparator(
        string value_)
    {
        Assert.Throws<ArgumentException>(
            () =>
                new AgentCapability(
                    value_));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("z")]
    [InlineData("0")]
    [InlineData("9")]
    public void Boundaries_AcceptsSingleValidCharacter(
        string singleChar_)
    {
        AgentCapability capability =
            new(singleChar_);

        Assert.Equal(
            singleChar_,
            capability.Value);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("-")]
    public void Boundaries_RejectsSingleInvalidSeparator(
        string separator_)
    {
        Assert.Throws<ArgumentException>(
            () =>
                new AgentCapability(
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

        AgentCapability capability =
            new(valid64);

        Assert.Equal(
            valid64,
            capability.Value);
    }

    [Fact]
    public void Boundaries_Rejects65Characters()
    {
        string invalid65 =
            new string('a', 65);

        Assert.Throws<ArgumentException>(
            () =>
                new AgentCapability(
                    invalid65));
    }

    [Fact]
    public void Constructor_PreservesExactValue()
    {
        const string input =
            "session.resume-v2";

        AgentCapability capability =
            new(input);

        Assert.Same(
            input,
            capability.Value);

        Assert.Equal(
            input,
            capability.ToString());
    }

    [Fact]
    public void Constructor_DoesNotNormalizeOrLowercase()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new AgentCapability(
                    "Structured-Output"));

        Assert.Throws<ArgumentException>(
            () =>
                new AgentCapability(
                    " structured-output "));
    }

    [Fact]
    public void ValueSemantics_Equality()
    {
        AgentCapability c1 =
            new("tools");
        AgentCapability c2 =
            new("tools");
        AgentCapability c3 =
            new("text");

        Assert.Equal(
            c1,
            c2);
        Assert.True(
            c1 == c2);
        Assert.False(
            c1 != c2);

        Assert.NotEqual(
            c1,
            c3);
        Assert.True(
            c1 != c3);
        Assert.False(
            c1 == c3);

        Assert.Equal(
            c1.GetHashCode(),
            c2.GetHashCode());
    }

    [Fact]
    public void AgentCapability_DoesNotImplementIComparable()
    {
        Assert.False(
            typeof(IComparable).IsAssignableFrom(typeof(AgentCapability)),
            "AgentCapability must not implement non-generic IComparable.");

        Assert.False(
            typeof(IComparable<AgentCapability>).IsAssignableFrom(typeof(AgentCapability)),
            "AgentCapability must not implement IComparable<AgentCapability>.");

        MethodInfo? compareTo =
            typeof(AgentCapability).GetMethod(
                "CompareTo",
                BindingFlags.Public |
                BindingFlags.Instance);

        Assert.Null(
            compareTo);
    }

    [Fact]
    public void AgentCapability_HasNoBuiltInCapabilityConstants()
    {
        FieldInfo[] publicStaticFields =
            typeof(AgentCapability).GetFields(
                BindingFlags.Public |
                BindingFlags.Static);

        Assert.Empty(
            publicStaticFields);
    }
}
