namespace AiRepoKit.Agents;

using System.Collections;

public sealed class AgentCapabilitySet :
    IReadOnlyCollection<AgentCapability>,
    IEquatable<AgentCapabilitySet>
{
    private static readonly AgentCapability[] _emptyCapabilities =
        [];

    public static AgentCapabilitySet Empty { get; } =
        new(Array.Empty<AgentCapability>());

    private readonly AgentCapability[] _capabilities;

    public int Count
    {
        get
        {
            return this._capabilities.Length;
        }
    }

    public AgentCapabilitySet()
        : this(Array.Empty<AgentCapability>())
    {
    }

    public AgentCapabilitySet(
        IEnumerable<AgentCapability> capabilities_)
    {
        ArgumentNullException.ThrowIfNull(
            capabilities_,
            nameof(capabilities_));

        Dictionary<string, AgentCapability> uniqueMap =
            new(StringComparer.Ordinal);

        foreach (AgentCapability capability in capabilities_)
        {
            if (capability is null)
            {
                throw new ArgumentException(
                    "Capability set cannot contain null elements.",
                    nameof(capabilities_));
            }

            uniqueMap.TryAdd(
                capability.Value,
                capability);
        }

        if (uniqueMap.Count == 0)
        {
            this._capabilities =
                _emptyCapabilities;

            return;
        }

        this._capabilities =
            uniqueMap
                .Values
                .OrderBy(
                    capability_ =>
                        capability_.Value,
                    StringComparer.Ordinal)
                .ToArray();
    }

    public bool Supports(
        AgentCapability capability_)
    {
        ArgumentNullException.ThrowIfNull(
            capability_,
            nameof(capability_));

        return this.BinarySearch(
            capability_.Value) >= 0;
    }

    public bool SupportsAll(
        AgentCapabilitySet requiredCapabilities_)
    {
        ArgumentNullException.ThrowIfNull(
            requiredCapabilities_,
            nameof(requiredCapabilities_));

        if (requiredCapabilities_.Count == 0)
        {
            return true;
        }

        if (this._capabilities.Length < requiredCapabilities_._capabilities.Length)
        {
            return false;
        }

        int thisIndex =
            0;
        int requiredIndex =
            0;

        while (thisIndex < this._capabilities.Length &&
               requiredIndex < requiredCapabilities_._capabilities.Length)
        {
            int comparison =
                string.CompareOrdinal(
                    this._capabilities[thisIndex].Value,
                    requiredCapabilities_._capabilities[requiredIndex].Value);

            if (comparison == 0)
            {
                thisIndex++;
                requiredIndex++;
            }
            else if (comparison < 0)
            {
                thisIndex++;
            }
            else
            {
                return false;
            }
        }

        return requiredIndex == requiredCapabilities_._capabilities.Length;
    }

    public IEnumerator<AgentCapability> GetEnumerator()
    {
        return ((IEnumerable<AgentCapability>)this._capabilities).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.GetEnumerator();
    }

    public bool Equals(
        AgentCapabilitySet? other_)
    {
        if (ReferenceEquals(
                this,
                other_))
        {
            return true;
        }

        if (other_ is null)
        {
            return false;
        }

        if (this._capabilities.Length != other_._capabilities.Length)
        {
            return false;
        }

        for (int i = 0; i < this._capabilities.Length; i++)
        {
            if (!string.Equals(
                    this._capabilities[i].Value,
                    other_._capabilities[i].Value,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(
        object? obj_)
    {
        return obj_ is AgentCapabilitySet other && this.Equals(other);
    }

    public override int GetHashCode()
    {
        HashCode hash =
            new();

        foreach (AgentCapability capability in this._capabilities)
        {
            hash.Add(
                capability.Value,
                StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(
        AgentCapabilitySet? left_,
        AgentCapabilitySet? right_)
    {
        return Equals(
            left_,
            right_);
    }

    public static bool operator !=(
        AgentCapabilitySet? left_,
        AgentCapabilitySet? right_)
    {
        return !Equals(
            left_,
            right_);
    }

    public override string ToString()
    {
        return $"[{string.Join(", ", this._capabilities.Select(capability_ => capability_.Value))}]";
    }

    private int BinarySearch(
        string value_)
    {
        int low =
            0;
        int high =
            this._capabilities.Length - 1;

        while (low <= high)
        {
            int mid =
                low + ((high - low) >> 1);
            int comparison =
                string.CompareOrdinal(
                    this._capabilities[mid].Value,
                    value_);

            if (comparison == 0)
            {
                return mid;
            }

            if (comparison < 0)
            {
                low =
                    mid + 1;
            }
            else
            {
                high =
                    mid - 1;
            }
        }

        return ~low;
    }
}
