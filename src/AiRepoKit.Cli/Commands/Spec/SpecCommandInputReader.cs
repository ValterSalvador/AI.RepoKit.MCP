using System.Text;
using System.Text.Json;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Persistence;

namespace AiRepoKit.Cli.Commands.Spec;

public static class SpecCommandInputReader
{
    private static readonly UTF8Encoding _strictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    public static T ReadCandidate<T>(string filePath_, SpecArtifactKind artifactKind_)
    {
        string fullPath = Path.GetFullPath(filePath_);
        if (!File.Exists(fullPath))
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ReadFailed,
                $"Candidate file '{filePath_}' does not exist.",
                artifactKind_);
        }

        FileInfo fileInfo = new(fullPath);
        if (fileInfo.Length > SpecWorkspace.MaximumArtifactSizeBytes)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ArtifactTooLarge,
                $"Candidate artifact '{filePath_}' exceeds the {SpecWorkspace.MaximumArtifactSizeBytes}-byte limit.",
                artifactKind_);
        }

        byte[] bytes;
        try
        {
            using FileStream stream = new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);

            if (stream.Length > SpecWorkspace.MaximumArtifactSizeBytes)
            {
                throw new SpecPersistenceException(
                    SpecPersistenceException.ArtifactTooLarge,
                    $"Candidate artifact '{filePath_}' exceeds the {SpecWorkspace.MaximumArtifactSizeBytes}-byte limit.",
                    artifactKind_);
            }

            byte[] buffer = new byte[SpecWorkspace.MaximumArtifactSizeBytes + 1];
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (totalRead > SpecWorkspace.MaximumArtifactSizeBytes)
            {
                throw new SpecPersistenceException(
                    SpecPersistenceException.ArtifactTooLarge,
                    $"Candidate artifact '{filePath_}' exceeds the {SpecWorkspace.MaximumArtifactSizeBytes}-byte limit.",
                    artifactKind_);
            }

            bytes = buffer[..totalRead];
        }
        catch (SpecPersistenceException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ReadFailed,
                $"Candidate artifact '{filePath_}' could not be read.",
                artifactKind_,
                innerException_: exception);
        }

        string json;
        try
        {
            int offset = HasUtf8Bom(bytes) ? 3 : 0;
            json = _strictUtf8.GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException exception)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.InvalidUtf8,
                $"Candidate artifact '{filePath_}' is not valid UTF-8.",
                artifactKind_,
                innerException_: exception);
        }

        try
        {
            return SpecJsonSerializer.Deserialize<T>(json);
        }
        catch (Exception exception) when (
            exception is JsonException or
            NotSupportedException or
            ArgumentException)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.InvalidJson,
                $"Candidate artifact '{filePath_}' is not valid spec JSON.",
                artifactKind_,
                innerException_: exception);
        }
    }

    private static bool HasUtf8Bom(byte[] bytes_)
    {
        return bytes_.Length >= 3 &&
               bytes_[0] == 0xEF &&
               bytes_[1] == 0xBB &&
               bytes_[2] == 0xBF;
    }
}
