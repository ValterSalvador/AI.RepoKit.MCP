namespace AiRepoKit.Orchestration;

internal static class WorkflowPersistenceAtomicWriter
{
    public static void WriteNew(
        string destinationPath_,
        byte[] payload_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            destinationPath_,
            nameof(destinationPath_));
        ArgumentNullException.ThrowIfNull(
            payload_,
            nameof(payload_));

        string directory =
            Path.GetDirectoryName(
                destinationPath_) ??
            throw new InvalidOperationException(
                "Persistence record path has no parent directory.");

        string fileName =
            Path.GetFileName(
                destinationPath_);

        string tempPath =
            Path.Combine(
                directory,
                $".{fileName}.{Guid.NewGuid():N}.tmp");

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

            File.Move(
                tempPath,
                destinationPath_,
                overwrite: false);
        }
        finally
        {
            try
            {
                if (File.Exists(
                        tempPath))
                {
                    File.Delete(
                        tempPath);
                }
            }
            catch
            {
            }
        }
    }
}
