using System.Security.Cryptography;
using System.Text;

namespace AiRepoKit.Spec.Persistence;

internal static class SpecWorkspaceWriteCoordinator
{
    private static readonly TimeSpan _waitTimeout =
        TimeSpan.FromSeconds(
            30);

    public static T Execute<T>(
        string specDirectory_,
        SpecArtifactKind artifactKind_,
        Func<T> action_)
    {
        try
        {
            return Execute(
                specDirectory_,
                action_);
        }
        catch (SpecPersistenceException exception) when (
            exception.ArtifactKind is null)
        {
            throw new SpecPersistenceException(
                exception.ErrorCode,
                exception.Message,
                artifactKind_,
                exception.ValidationErrors,
                exception.InnerException);
        }
    }

    public static T Execute<T>(
        string specDirectory_,
        Func<T> action_)
    {
        ArgumentNullException.ThrowIfNull(
            action_);

        string identity =
            Path.GetFullPath(
                specDirectory_);

        if (OperatingSystem.IsWindows())
        {
            identity =
                identity.ToUpperInvariant();
        }

        string mutexName =
            "AIRepoKit.Spec." +
            Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            identity)))
                .ToLowerInvariant();

        try
        {
            using Mutex mutex =
                new(
                    initiallyOwned: false,
                    mutexName);
            bool ownsMutex;

            try
            {
                ownsMutex =
                    mutex.WaitOne(
                        _waitTimeout);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex =
                    true;
            }

            if (!ownsMutex)
            {
                throw new SpecPersistenceException(
                    SpecPersistenceException.WriteFailed,
                    "The spec workspace write lock could not be acquired within the allowed time.");
            }

            try
            {
                return action_();
            }
            finally
            {
                mutex.ReleaseMutex();
            }
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
                SpecPersistenceException.WriteFailed,
                "The spec workspace write lock could not be used.",
                innerException_: exception);
        }
    }
}
