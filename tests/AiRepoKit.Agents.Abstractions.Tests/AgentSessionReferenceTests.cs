using System.Reflection;
using AiRepoKit.Agents;
using Xunit;

namespace AiRepoKit.Agents.Abstractions.Tests;

public sealed class AgentSessionReferenceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("   \r\n   ")]
    public void Constructor_RejectsNullEmptyOrAllWhitespace(
        string? invalidValue_)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new AgentSessionReference(
                    invalidValue_!));
    }

    [Theory]
    [InlineData("session-123")]
    [InlineData(" session-123 ")]
    [InlineData("\tsession-tab\t")]
    [InlineData("urn:session:antigravity:task-99")]
    public void Constructor_PreservesExactOriginalValue(
        string value_)
    {
        AgentSessionReference sessionRef =
            new(value_);

        Assert.Equal(
            value_,
            sessionRef.Value);

        Assert.Equal(
            value_,
            sessionRef.ToString());
    }

    [Theory]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("  surrounded  ")]
    [InlineData("\t\r\n mixed \r\n\t")]
    public void Constructor_PreservesExactLeadingAndTrailingWhitespace_WhenNonWhitespaceContentExists(
        string value_)
    {
        AgentSessionReference sessionRef =
            new(value_);

        Assert.Equal(
            value_,
            sessionRef.Value);

        Assert.Equal(
            value_,
            sessionRef.ToString());
    }

    [Fact]
    public void Constructor_DoesNotImposeArbitraryMaximumLength()
    {
        string longSession =
            "session-" +
            new string('x', 1000);

        AgentSessionReference sessionRef =
            new(longSession);

        Assert.Equal(
            longSession,
            sessionRef.Value);
    }

    [Fact]
    public void ValueSemantics_Equality()
    {
        AgentSessionReference s1 =
            new("session-1");
        AgentSessionReference s2 =
            new("session-1");
        AgentSessionReference s3 =
            new("session-2");

        Assert.Equal(
            s1,
            s2);
        Assert.True(
            s1 == s2);
        Assert.False(
            s1 != s2);

        Assert.NotEqual(
            s1,
            s3);
        Assert.True(
            s1 != s3);

        AgentSessionReference sTrimmed =
            new("session-1");
        AgentSessionReference sUntrimmed =
            new(" session-1 ");

        Assert.NotEqual(
            sTrimmed,
            sUntrimmed);
        Assert.True(
            sTrimmed != sUntrimmed);

        Assert.Equal(
            s1.GetHashCode(),
            s2.GetHashCode());
    }

    [Fact]
    public void AgentSessionReference_ExposesNoPublicTryParse()
    {
        MethodInfo? tryParse =
            typeof(AgentSessionReference).GetMethod(
                "TryParse",
                BindingFlags.Public |
                BindingFlags.Static);

        Assert.Null(
            tryParse);

        MethodInfo? parse =
            typeof(AgentSessionReference).GetMethod(
                "Parse",
                BindingFlags.Public |
                BindingFlags.Static);

        Assert.Null(
            parse);
    }

    [Fact]
    public void AgentSessionReference_DoesNotImplementIComparable()
    {
        Assert.False(
            typeof(IComparable).IsAssignableFrom(typeof(AgentSessionReference)),
            "AgentSessionReference must not implement non-generic IComparable.");

        Assert.False(
            typeof(IComparable<AgentSessionReference>).IsAssignableFrom(typeof(AgentSessionReference)),
            "AgentSessionReference must not implement IComparable<AgentSessionReference>.");

        MethodInfo? compareTo =
            typeof(AgentSessionReference).GetMethod(
                "CompareTo",
                BindingFlags.Public |
                BindingFlags.Instance);

        Assert.Null(
            compareTo);
    }

    [Fact]
    public void AgentSessionReference_IsNotValueType_AndCannotMaterializeInvalidDefaultInstance()
    {
        Assert.False(
            typeof(AgentSessionReference).IsValueType,
            "AgentSessionReference must not be a value type capable of producing an invalid default instance.");

        AgentSessionReference? defaultRef =
            default;

        Assert.Null(
            defaultRef);

        ConstructorInfo? parameterlessCtor =
            typeof(AgentSessionReference).GetConstructor(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance,
                Type.EmptyTypes);

        Assert.Null(
            parameterlessCtor);

        Assert.Throws<MissingMethodException>(
            () =>
                Activator.CreateInstance<AgentSessionReference>());
    }
}
