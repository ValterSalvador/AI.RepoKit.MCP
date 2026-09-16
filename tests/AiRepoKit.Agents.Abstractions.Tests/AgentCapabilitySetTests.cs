using System.Collections;
using System.Reflection;
using AiRepoKit.Agents;
using Xunit;

namespace AiRepoKit.Agents.Abstractions.Tests;

public sealed class AgentCapabilitySetTests
{
    [Fact]
    public void Constructor_RejectsNullCollection()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                new AgentCapabilitySet(
                    null!));
    }

    [Fact]
    public void Constructor_RejectsNullMember()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new AgentCapabilitySet(
                    [new AgentCapability("text"), null!]));
    }

    [Fact]
    public void Constructor_AcceptsEmptyCollection()
    {
        AgentCapabilitySet set =
            new([]);

        int count =
            set.Count;

        Assert.Equal(
            0,
            count);
        Assert.Empty(
            set);
    }

    [Fact]
    public void EmptyProperty_ReturnsValidEmptySet()
    {
        AgentCapabilitySet empty =
            AgentCapabilitySet.Empty;

        Assert.NotNull(
            empty);

        int count =
            empty.Count;

        Assert.Equal(
            0,
            count);
        Assert.Empty(
            empty);
    }

    [Fact]
    public void Constructor_CollapsesDuplicatesToOne()
    {
        AgentCapability c1 =
            new("tools");
        AgentCapability c2 =
            new("tools");

        AgentCapabilitySet set =
            new([c1, c2]);

        int count =
            set.Count;

        Assert.Equal(
            1,
            count);
        Assert.Single(
            set);

        AgentCapability[] array =
            set.ToArray();

        Assert.Single(
            array);
        Assert.Equal(
            "tools",
            array[0].Value);
    }

    [Fact]
    public void Enumeration_ProducesCanonicalOrdinalOrder()
    {
        AgentCapabilitySet set =
            new(
            [
                new AgentCapability("zeta"),
                new AgentCapability("alpha"),
                new AgentCapability("tools"),
                new AgentCapability("alpha")
            ]);

        string[] values =
            set.Select(c_ => c_.Value).ToArray();

        Assert.Equal(
            ["alpha", "tools", "zeta"],
            values);
    }

    [Fact]
    public void Enumeration_IsIndependentOfInsertionOrder()
    {
        AgentCapabilitySet set1 =
            new(
            [
                new AgentCapability("zeta"),
                new AgentCapability("tools"),
                new AgentCapability("alpha")
            ]);

        AgentCapabilitySet set2 =
            new(
            [
                new AgentCapability("tools"),
                new AgentCapability("alpha"),
                new AgentCapability("zeta")
            ]);

        Assert.Equal(
            set1.Select(c_ => c_.Value).ToArray(),
            set2.Select(c_ => c_.Value).ToArray());
    }

    [Fact]
    public void Count_RepresentsUniqueCapabilities()
    {
        AgentCapabilitySet set =
            new(
            [
                new AgentCapability("alpha"),
                new AgentCapability("beta"),
                new AgentCapability("alpha"),
                new AgentCapability("gamma"),
                new AgentCapability("beta")
            ]);

        Assert.Equal(
            3,
            set.Count);
    }

    [Fact]
    public void CallerSourceCollectionMutation_DoesNotAffectConstructedSet()
    {
        List<AgentCapability> source =
        [
            new AgentCapability("alpha")
        ];

        AgentCapabilitySet set =
            new(source);

        source.Add(
            new AgentCapability("beta"));
        source.Clear();

        int count =
            set.Count;

        Assert.Equal(
            1,
            count);
        Assert.Single(
            set);
        Assert.True(
            set.Supports(new AgentCapability("alpha")));
        Assert.False(
            set.Supports(new AgentCapability("beta")));
    }

    [Fact]
    public void Supports_ReturnsTrueForPresentCapability()
    {
        AgentCapabilitySet set =
            new(
            [
                new AgentCapability("alpha"),
                new AgentCapability("tools"),
                new AgentCapability("zeta")
            ]);

        Assert.True(
            set.Supports(new AgentCapability("tools")));
        Assert.True(
            set.Supports(new AgentCapability("alpha")));
        Assert.True(
            set.Supports(new AgentCapability("zeta")));
    }

    [Fact]
    public void Supports_ReturnsFalseForMissingCapability()
    {
        AgentCapabilitySet set =
            new(
            [
                new AgentCapability("alpha"),
                new AgentCapability("zeta")
            ]);

        Assert.False(
            set.Supports(new AgentCapability("tools")));
        Assert.False(
            set.Supports(new AgentCapability("beta")));
    }

    [Fact]
    public void Supports_RejectsNull()
    {
        AgentCapabilitySet set =
            new([new AgentCapability("tools")]);

        Assert.Throws<ArgumentNullException>(
            () =>
                set.Supports(
                    null!));
    }

    [Fact]
    public void SupportsAll_ReturnsTrueForFullMatch()
    {
        AgentCapabilitySet available =
            new(
            [
                new AgentCapability("alpha"),
                new AgentCapability("beta"),
                new AgentCapability("tools"),
                new AgentCapability("zeta")
            ]);

        AgentCapabilitySet required =
            new(
            [
                new AgentCapability("alpha"),
                new AgentCapability("tools")
            ]);

        Assert.True(
            available.SupportsAll(required));
    }

    [Fact]
    public void SupportsAll_ReturnsFalseForPartialOrMissingMatch()
    {
        AgentCapabilitySet available =
            new(
            [
                new AgentCapability("alpha"),
                new AgentCapability("tools")
            ]);

        AgentCapabilitySet required =
            new(
            [
                new AgentCapability("alpha"),
                new AgentCapability("zeta")
            ]);

        Assert.False(
            available.SupportsAll(required));
    }

    [Fact]
    public void SupportsAll_EmptyRequired_ReturnsTrue()
    {
        AgentCapabilitySet available =
            new([new AgentCapability("tools")]);

        AgentCapabilitySet empty =
            AgentCapabilitySet.Empty;

        Assert.True(
            available.SupportsAll(empty));
    }

    [Fact]
    public void SupportsAll_EmptySupportsAllEmpty_ReturnsTrue()
    {
        AgentCapabilitySet empty1 =
            AgentCapabilitySet.Empty;
        AgentCapabilitySet empty2 =
            new([]);

        Assert.True(
            empty1.SupportsAll(empty2));
    }

    [Fact]
    public void SupportsAll_EmptySupportsAllNonEmpty_ReturnsFalse()
    {
        AgentCapabilitySet empty =
            AgentCapabilitySet.Empty;

        AgentCapabilitySet nonEmpty =
            new([new AgentCapability("tools")]);

        Assert.False(
            empty.SupportsAll(nonEmpty));
    }

    [Fact]
    public void SupportsAll_RejectsNull()
    {
        AgentCapabilitySet set =
            new([new AgentCapability("tools")]);

        Assert.Throws<ArgumentNullException>(
            () =>
                set.SupportsAll(
                    null!));
    }

    [Fact]
    public void Consumer_CannotMutateInternalSetThroughExposedApi()
    {
        Type setType =
            typeof(AgentCapabilitySet);

        Assert.False(
            typeof(IList).IsAssignableFrom(setType),
            "AgentCapabilitySet must not implement IList.");

        Assert.False(
            typeof(IList<AgentCapability>).IsAssignableFrom(setType),
            "AgentCapabilitySet must not implement IList<AgentCapability>.");

        Assert.False(
            typeof(ICollection<AgentCapability>).IsAssignableFrom(setType),
            "AgentCapabilitySet must not implement ICollection<AgentCapability>.");

        Assert.Null(
            setType.GetMethod("Add"));
        Assert.Null(
            setType.GetMethod("Remove"));
        Assert.Null(
            setType.GetMethod("Clear"));
    }

    [Fact]
    public void LogicalEquality_EqualSetsWithDifferentInputOrder()
    {
        AgentCapabilitySet set1 =
            new(
            [
                new AgentCapability("zeta"),
                new AgentCapability("alpha")
            ]);

        AgentCapabilitySet set2 =
            new(
            [
                new AgentCapability("alpha"),
                new AgentCapability("zeta")
            ]);

        Assert.Equal(
            set1,
            set2);
        Assert.True(
            set1 == set2);
        Assert.False(
            set1 != set2);
        Assert.True(
            set1.Equals((object)set2));
    }

    [Fact]
    public void LogicalEquality_DifferingSetsAreNotEqual()
    {
        AgentCapabilitySet set1 =
            new([new AgentCapability("alpha"), new AgentCapability("tools")]);
        AgentCapabilitySet set2 =
            new([new AgentCapability("alpha"), new AgentCapability("zeta")]);

        Assert.NotEqual(
            set1,
            set2);
        Assert.False(
            set1 == set2);
        Assert.True(
            set1 != set2);
    }

    [Fact]
    public void LogicalEquality_HashCodesAreCompatible()
    {
        AgentCapabilitySet set1 =
            new(
            [
                new AgentCapability("zeta"),
                new AgentCapability("alpha"),
                new AgentCapability("tools")
            ]);

        AgentCapabilitySet set2 =
            new(
            [
                new AgentCapability("tools"),
                new AgentCapability("alpha"),
                new AgentCapability("zeta")
            ]);

        Assert.Equal(
            set1.GetHashCode(),
            set2.GetHashCode());
    }

    [Fact]
    public void NonGenericEnumerator_WorksCorrectly()
    {
        AgentCapabilitySet set =
            new([new AgentCapability("alpha"), new AgentCapability("beta")]);

        IEnumerable nonGeneric =
            set;

        IEnumerator enumerator =
            nonGeneric.GetEnumerator();

        List<string> values =
            [];

        while (enumerator.MoveNext())
        {
            if (enumerator.Current is AgentCapability cap)
            {
                values.Add(
                    cap.Value);
            }
        }

        Assert.Equal(
            ["alpha", "beta"],
            values);
    }

    [Fact]
    public void ToString_ReturnsFormattedRepresentation()
    {
        AgentCapabilitySet set =
            new([new AgentCapability("tools"), new AgentCapability("text")]);

        string representation =
            set.ToString();

        Assert.Equal(
            "[text, tools]",
            representation);
    }

    [Fact]
    public void AgentCapabilitySet_DoesNotImplementIComparable()
    {
        Assert.False(
            typeof(IComparable).IsAssignableFrom(typeof(AgentCapabilitySet)),
            "AgentCapabilitySet must not implement non-generic IComparable.");

        Assert.False(
            typeof(IComparable<AgentCapabilitySet>).IsAssignableFrom(typeof(AgentCapabilitySet)),
            "AgentCapabilitySet must not implement IComparable<AgentCapabilitySet>.");
    }
}
