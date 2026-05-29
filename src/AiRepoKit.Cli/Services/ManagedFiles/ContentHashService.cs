using System.Security.Cryptography;
using System.Text;

namespace AiRepoKit.Cli.Services.ManagedFiles;

public sealed class ContentHashService
{
    public string Normalize(string content_)
    {
        if (string.IsNullOrEmpty(content_))
        {
            return string.Empty;
        }

        string normalized = content_.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return normalized.Length > 0 && normalized[0] == '\uFEFF' ? normalized[1..] : normalized;
    }

    public string ComputeSha256(string content_)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(this.Normalize(content_));
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    public long GetSizeBytes(string content_)
    {
        return Encoding.UTF8.GetByteCount(this.Normalize(content_));
    }
}
