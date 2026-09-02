using System.Globalization;

namespace AiRepoKit.Spec;

public readonly record struct ArtifactRevision :
    IComparable<ArtifactRevision>
{
    public int Value
    {
        get;
    }

    public ArtifactRevision(
        int value_)
    {
        if (value_ < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value_),
                value_,
                "Artifact revision must be greater than zero.");
        }

        this.Value =
            value_;
    }

    public bool IsValid =>
        this.Value > 0;

    public int CompareTo(
        ArtifactRevision other_)
    {
        return this.Value.CompareTo(
            other_.Value);
    }

    public override string ToString()
    {
        return this.Value.ToString(
            CultureInfo.InvariantCulture);
    }
}
