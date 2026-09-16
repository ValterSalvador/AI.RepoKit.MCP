namespace AiRepoKit.Agents.Abstractions.Tests;

using System.Reflection;
using AiRepoKit.Agents;
using Xunit;

public sealed class StructuredOutputContractTests
{
    [Fact]
    public void Type_IsReferenceTypeAndSealed()
    {
        Assert.False(
            typeof(StructuredOutputContract).IsValueType);

        Assert.True(
            typeof(StructuredOutputContract).IsClass);

        Assert.True(
            typeof(StructuredOutputContract).IsSealed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(" \r\n ")]
    public void Constructor_RejectsNullEmptyOrWhitespace(
        string? invalidJson_)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new StructuredOutputContract(
                    invalidJson_!));
    }

    [Theory]
    [InlineData("{ not json }")]
    [InlineData("{ \"unclosed\": ")]
    [InlineData("invalid")]
    [InlineData("{ \"a\": 1 } trailing")]
    public void Constructor_RejectsMalformedJson(
        string malformedJson_)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new StructuredOutputContract(
                    malformedJson_));
    }

    [Fact]
    public void Constructor_AcceptsValidJsonObject()
    {
        string json =
            "{\"type\":\"object\"}";

        StructuredOutputContract contract =
            new(json);

        Assert.Equal(
            json,
            contract.JsonSchema);
    }

    [Fact]
    public void Constructor_AcceptsValidJsonWithWhitespace()
    {
        string json =
            "{\n  \"type\": \"object\",\n  \"properties\": {}\n}";

        StructuredOutputContract contract =
            new(json);

        Assert.Equal(
            json,
            contract.JsonSchema);
    }

    [Fact]
    public void Constructor_PreservesExactOriginalText()
    {
        string json =
            "{\r\n  \"hello\": \"world\"\r\n}";

        StructuredOutputContract contract =
            new(json);

        Assert.Equal(
            json,
            contract.JsonSchema);
    }

    [Fact]
    public void Constructor_PreservesPropertyOrderInStoredString()
    {
        string json =
            "{\"z\":1,\"a\":2,\"m\":3}";

        StructuredOutputContract contract =
            new(json);

        Assert.Equal(
            json,
            contract.JsonSchema);
    }

    [Fact]
    public void Constructor_PreservesSurroundingJsonWhitespace()
    {
        string json =
            "   \t\r\n{\"type\":\"object\"}\n   ";

        StructuredOutputContract contract =
            new(json);

        Assert.Equal(
            json,
            contract.JsonSchema);
    }

    [Fact]
    public void Constructor_ImposesNoSemanticJsonSchemaRequirementBeyondSyntaxValidation()
    {
        string arbitraryJson =
            "{\"not_a_schema\": 123, \"foo\": [true, null]}";

        StructuredOutputContract contract =
            new(arbitraryJson);

        Assert.Equal(
            arbitraryJson,
            contract.JsonSchema);
    }

    [Fact]
    public void Equality_FollowsExactStoredJsonSchemaValue()
    {
        string json1 =
            "{\"key\":\"value\"}";
        string json2 =
            "{\"key\":\"value\"}";
        string json3 =
            "{\"key\": \"value\"}";

        StructuredOutputContract c1 =
            new(json1);
        StructuredOutputContract c2 =
            new(json2);
        StructuredOutputContract c3 =
            new(json3);

        Assert.Equal(
            c1,
            c2);

        Assert.True(
            c1 == c2);

        Assert.False(
            c1 != c2);

        Assert.Equal(
            c1.GetHashCode(),
            c2.GetHashCode());

        Assert.NotEqual(
            c1,
            c3);

        Assert.False(
            c1 == c3);

        Assert.True(
            c1 != c3);
    }

    [Fact]
    public void PublicSurface_ContainsOnlyJsonSchemaProperty()
    {
        PropertyInfo[] properties =
            typeof(StructuredOutputContract).GetProperties(
                BindingFlags.Public |
                BindingFlags.Instance);

        Assert.Single(
            properties);

        Assert.Equal(
            "JsonSchema",
            properties[0].Name);

        Assert.Equal(
            typeof(string),
            properties[0].PropertyType);

        FieldInfo[] fields =
            typeof(StructuredOutputContract).GetFields(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static);

        Assert.Empty(
            fields);
    }
}
