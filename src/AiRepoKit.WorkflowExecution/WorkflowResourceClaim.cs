namespace AiRepoKit.WorkflowExecution;

using System.Collections.ObjectModel;
using System.Text;

public sealed record WorkflowResourceClaim
{
    private static readonly UTF8Encoding _strictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string _scheme;
    private readonly string _root;
    private readonly string[] _segments;
    private readonly ReadOnlyCollection<string> _readOnlySegments;

    public string ResourceId
    {
        get;
    }

    public WorkflowResourceAccessKind AccessKind
    {
        get;
    }

    internal string Scheme =>
        this._scheme;

    internal string Root =>
        this._root;

    internal IReadOnlyList<string> Segments =>
        this._readOnlySegments;

    public WorkflowResourceClaim(
        string resourceId_,
        WorkflowResourceAccessKind accessKind_)
    {
        ArgumentNullException.ThrowIfNull(
            resourceId_,
            nameof(resourceId_));

        if (string.IsNullOrWhiteSpace(resourceId_))
        {
            throw new ArgumentException(
                "ResourceId cannot be empty or whitespace.",
                nameof(resourceId_));
        }

        if (resourceId_.Contains('\0'))
        {
            throw new ArgumentException(
                "ResourceId cannot contain NUL (U+0000) characters.",
                nameof(resourceId_));
        }

        if (accessKind_ != WorkflowResourceAccessKind.SharedRead &&
            accessKind_ != WorkflowResourceAccessKind.ExclusiveWrite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accessKind_),
                accessKind_,
                "Undefined or invalid WorkflowResourceAccessKind enum value.");
        }

        try
        {
            _ = _strictUtf8.GetByteCount(resourceId_);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "ResourceId contains invalid Unicode.",
                nameof(resourceId_),
                exception);
        }

        try
        {
            if (!resourceId_.IsNormalized(NormalizationForm.FormC))
            {
                throw new ArgumentException(
                    "ResourceId must already be normalized in Unicode NFC.",
                    nameof(resourceId_));
            }
        }
        catch (ArgumentException exception) when (exception.ParamName != nameof(resourceId_))
        {
            throw new ArgumentException(
                "ResourceId contains invalid Unicode.",
                nameof(resourceId_),
                exception);
        }

        if (resourceId_.StartsWith("fs:", StringComparison.Ordinal))
        {
            this._scheme = "fs";
            string path = resourceId_.Substring(3);

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "ResourceId 'fs:' path cannot be empty or whitespace.",
                    nameof(resourceId_));
            }

            if (path.Contains('\\', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "ResourceId 'fs:' path cannot contain backslashes.",
                    nameof(resourceId_));
            }

            string root;
            string subpath;

            if (path.StartsWith('/'))
            {
                root = "fs:/";
                subpath = path.Substring(1);
            }
            else if (path.Length >= 3 &&
                     char.IsAsciiLetterUpper(path[0]) &&
                     path[1] == ':' &&
                     path[2] == '/')
            {
                root = $"fs:{path[0]}:/";
                subpath = path.Substring(3);
            }
            else
            {
                throw new ArgumentException(
                    "ResourceId 'fs:' path must be fully qualified and canonical.",
                    nameof(resourceId_));
            }

            this._root = root;

            if (string.IsNullOrEmpty(subpath))
            {
                this._segments = Array.Empty<string>();
            }
            else
            {
                if (subpath[0] == '/')
                {
                    throw new ArgumentException(
                        "ResourceId contains repeated path separators.",
                        nameof(resourceId_));
                }

                if (subpath[^1] == '/')
                {
                    throw new ArgumentException(
                        "ResourceId cannot contain a trailing path separator.",
                        nameof(resourceId_));
                }

                string[] segments = subpath.Split('/');
                for (int i = 0; i < segments.Length; i++)
                {
                    string segment = segments[i];
                    if (string.IsNullOrEmpty(segment))
                    {
                        throw new ArgumentException(
                            "ResourceId contains repeated path separators.",
                            nameof(resourceId_));
                    }

                    if (string.Equals(segment, ".", StringComparison.Ordinal) ||
                        string.Equals(segment, "..", StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            "ResourceId cannot contain relative dot ('.' or '..') segments.",
                            nameof(resourceId_));
                    }
                }

                this._segments = segments;
            }

            this._readOnlySegments = Array.AsReadOnly(this._segments);
        }
        else if (resourceId_.StartsWith("logical:", StringComparison.Ordinal))
        {
            this._scheme = "logical";
            string id = resourceId_.Substring(8);

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "ResourceId 'logical:' identifier cannot be empty or whitespace.",
                    nameof(resourceId_));
            }

            if (id.Contains('\\', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "ResourceId 'logical:' identifier cannot contain backslashes.",
                    nameof(resourceId_));
            }

            if (id.StartsWith("/", StringComparison.Ordinal) ||
                id.EndsWith("/", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "ResourceId 'logical:' identifier cannot start or end with a slash.",
                    nameof(resourceId_));
            }

            string[] segments = id.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (string.IsNullOrWhiteSpace(segment))
                {
                    throw new ArgumentException(
                        "ResourceId 'logical:' identifier cannot contain empty segments.",
                        nameof(resourceId_));
                }

                if (string.Equals(segment, ".", StringComparison.Ordinal) ||
                    string.Equals(segment, "..", StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "ResourceId 'logical:' identifier cannot contain relative dot ('.' or '..') segments.",
                        nameof(resourceId_));
                }
            }

            this._root = "logical:";
            this._segments = segments;
            this._readOnlySegments = Array.AsReadOnly(this._segments);
        }
        else
        {
            throw new ArgumentException(
                "ResourceId must use either 'fs:' or 'logical:' scheme.",
                nameof(resourceId_));
        }

        this.ResourceId = resourceId_;
        this.AccessKind = accessKind_;
    }

    public bool Equals(
        WorkflowResourceClaim? other_)
    {
        if (other_ is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other_))
        {
            return true;
        }

        return string.Equals(this.ResourceId, other_.ResourceId, StringComparison.Ordinal) &&
               this.AccessKind == other_.AccessKind;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(this.ResourceId),
            this.AccessKind);
    }

    internal bool OverlapsHierarchicallyWith(
        WorkflowResourceClaim other_)
    {
        ArgumentNullException.ThrowIfNull(other_);

        if (!string.Equals(this._scheme, other_._scheme, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(this._root, other_._root, StringComparison.Ordinal))
        {
            return false;
        }

        string[] seg1 = this._segments;
        string[] seg2 = other_._segments;

        int minLen = Math.Min(seg1.Length, seg2.Length);
        for (int i = 0; i < minLen; i++)
        {
            if (!string.Equals(seg1[i], seg2[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return seg1.Length != seg2.Length;
    }
}
