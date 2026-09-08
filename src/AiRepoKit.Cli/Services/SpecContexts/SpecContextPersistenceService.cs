using System.Text;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Persistence;

namespace AiRepoKit.Cli.Services.SpecContexts;

public sealed class SpecContextPersistenceService :
    ISpecContextPersistenceService
{
    public const string RelativeDirectory =
        ".ai/generated/spec-context";

    public const int MaximumSpecContextSizeBytes =
        1_048_576;

    private static readonly UTF8Encoding _utf8Encoding =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly FileSystemService _fileSystemService;

    public SpecContextPersistenceService()
        : this(new FileSystemService())
    {
    }

    internal SpecContextPersistenceService(
        FileSystemService fileSystemService_)
    {
        this._fileSystemService =
            fileSystemService_ ??
            throw new ArgumentNullException(nameof(fileSystemService_));
    }

    public string Persist(
        string repoRoot_,
        SpecContext specContext_)
    {
        ArgumentNullException.ThrowIfNull(specContext_);

        if (string.IsNullOrWhiteSpace(repoRoot_))
        {
            throw new ArgumentException(
                "Repository root must not be blank.",
                nameof(repoRoot_));
        }

        string normalizedRoot =
            Path.GetFullPath(repoRoot_);

        if (!Directory.Exists(normalizedRoot))
        {
            throw new DirectoryNotFoundException(
                $"Repository root directory was not found: {normalizedRoot}");
        }

        if (!SpecId.IsValid(specContext_.SpecId))
        {
            throw new ArgumentException(
                $"Spec ID is invalid: '{specContext_.SpecId}'.",
                nameof(specContext_));
        }

        SpecId specId =
            new(specContext_.SpecId);

        IReadOnlyList<AiRepoKit.Spec.SpecValidationError> errors =
            SpecContextValidator.Validate(specContext_);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "SpecContext validation failed: " +
                string.Join(
                    " | ",
                    errors.Select(error_ => $"{error_.Code}: {error_.Message}")));
        }

        string json =
            SpecJsonSerializer.Serialize(specContext_);

        byte[] bytes =
            _utf8Encoding.GetBytes(json);

        if (bytes.Length > MaximumSpecContextSizeBytes)
        {
            throw new InvalidOperationException(
                $"Serialized SpecContext payload size ({bytes.Length} bytes) exceeds maximum allowed size ({MaximumSpecContextSizeBytes} bytes).");
        }

        string aiDir =
            Path.Combine(normalizedRoot, ".ai");
        string generatedDir =
            Path.Combine(aiDir, "generated");
        string specContextDir =
            Path.Combine(generatedDir, "spec-context");
        string finalFilePath =
            Path.Combine(specContextDir, $"{specId.Value}.json");
        string tempFilePath =
            Path.Combine(specContextDir, $"{specId.Value}.json.tmp");

        this.ValidateContainment(normalizedRoot, aiDir);
        this.ValidateContainment(normalizedRoot, generatedDir);
        this.ValidateContainment(normalizedRoot, specContextDir);
        this.ValidateContainment(normalizedRoot, finalFilePath);
        this.ValidateContainment(normalizedRoot, tempFilePath);

        this.CheckExistingPathBeforeCreation(aiDir, ".ai", expectDirectory_: true);
        this.CheckExistingPathBeforeCreation(generatedDir, ".ai/generated", expectDirectory_: true);
        this.CheckExistingPathBeforeCreation(specContextDir, ".ai/generated/spec-context", expectDirectory_: true);
        this.CheckExistingPathBeforeCreation(finalFilePath, $"{specId.Value}.json", expectDirectory_: false);
        this.CheckExistingPathBeforeCreation(tempFilePath, $"{specId.Value}.json.tmp", expectDirectory_: false);

        if (File.Exists(finalFilePath))
        {
            byte[] existingBytes =
                File.ReadAllBytes(finalFilePath);

            if (existingBytes.AsSpan().SequenceEqual(bytes))
            {
                return $"{RelativeDirectory}/{specId.Value}.json";
            }
        }

        this.EnsureDirectory(normalizedRoot, aiDir, ".ai");
        this.EnsureDirectory(normalizedRoot, generatedDir, ".ai/generated");
        this.EnsureDirectory(normalizedRoot, specContextDir, ".ai/generated/spec-context");

        if (Directory.Exists(tempFilePath))
        {
            throw new InvalidOperationException(
                $"Temporary file path is occupied by a directory: {tempFilePath}");
        }

        if (File.Exists(tempFilePath))
        {
            RejectReparsePoint(tempFilePath);
            File.Delete(tempFilePath);
        }

        try
        {
            File.WriteAllBytes(tempFilePath, bytes);
            this.ValidateContainment(normalizedRoot, tempFilePath);
            RejectReparsePoint(tempFilePath);

            if (Directory.Exists(finalFilePath))
            {
                throw new InvalidOperationException(
                    $"Final output path is occupied by a directory: {finalFilePath}");
            }

            if (File.Exists(finalFilePath))
            {
                this.ValidateContainment(normalizedRoot, finalFilePath);
                RejectReparsePoint(finalFilePath);
                File.Move(tempFilePath, finalFilePath, overwrite: true);
            }
            else
            {
                File.Move(tempFilePath, finalFilePath);
            }
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                }
                catch
                {
                }
            }
        }

        return $"{RelativeDirectory}/{specId.Value}.json";
    }

    private void ValidateContainment(
        string rootPath_,
        string path_)
    {
        if (!this._fileSystemService.IsInsideRoot(rootPath_, path_))
        {
            throw new InvalidOperationException(
                $"Path is outside repository root: {path_}");
        }
    }

    private void CheckExistingPathBeforeCreation(
        string path_,
        string displaySegment_,
        bool expectDirectory_)
    {
        RejectReparsePoint(path_);

        if (expectDirectory_)
        {
            if (File.Exists(path_))
            {
                throw new InvalidOperationException(
                    $"Path component '{displaySegment_}' is occupied by a file: {path_}");
            }
        }
        else
        {
            if (Directory.Exists(path_))
            {
                throw new InvalidOperationException(
                    $"Output path component '{displaySegment_}' is occupied by a directory: {path_}");
            }
        }
    }

    private void EnsureDirectory(
        string rootPath_,
        string directoryPath_,
        string displaySegment_)
    {
        RejectReparsePoint(directoryPath_);

        if (File.Exists(directoryPath_))
        {
            throw new InvalidOperationException(
                $"Path component '{displaySegment_}' is occupied by a file: {directoryPath_}");
        }

        if (!Directory.Exists(directoryPath_))
        {
            Directory.CreateDirectory(directoryPath_);
        }

        this.ValidateContainment(rootPath_, directoryPath_);
        RejectReparsePoint(directoryPath_);

        if (!Directory.Exists(directoryPath_))
        {
            throw new InvalidOperationException(
                $"Path component '{displaySegment_}' is not a directory: {directoryPath_}");
        }
    }

    private static void RejectReparsePoint(
        string path_)
    {
        try
        {
            FileAttributes attributes =
                File.GetAttributes(path_);

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"SpecContext path component is a symbolic link or reparse point: {path_}");
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
