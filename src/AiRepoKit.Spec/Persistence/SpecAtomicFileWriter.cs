namespace AiRepoKit.Spec.Persistence;

internal static class SpecAtomicFileWriter
{
    public static void Write(
        string destinationPath_,
        byte[] payload_,
        Action revalidateDestination_)
    {
        string directory =
            Path.GetDirectoryName(
                destinationPath_) ??
            throw new InvalidOperationException(
                "The canonical artifact path has no parent directory.");
        string canonicalFileName =
            Path.GetFileName(
                destinationPath_);
        string tempPath =
            Path.Combine(
                directory,
                $".{canonicalFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (
                FileStream stream =
                    new(
                        tempPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 4096,
                        FileOptions.SequentialScan))
            {
                stream.Write(
                    payload_);
                stream.Flush(
                    flushToDisk: true);
            }

            revalidateDestination_();

            File.Move(
                tempPath,
                destinationPath_,
                overwrite: true);
        }
        catch (SpecPersistenceException)
        {
            DeleteTempFile(
                tempPath);

            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException)
        {
            DeleteTempFile(
                tempPath);

            throw new SpecPersistenceException(
                SpecPersistenceException.WriteFailed,
                "The canonical spec artifact could not be replaced atomically.",
                innerException_: exception);
        }
        catch
        {
            DeleteTempFile(
                tempPath);

            throw;
        }
    }

    private static void DeleteTempFile(
        string tempPath_)
    {
        try
        {
            File.Delete(
                tempPath_);
        }
        catch
        {
        }
    }
}
