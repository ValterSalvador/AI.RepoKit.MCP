using System.Text;

namespace AiRepoKit.Agents.Codex;

internal static class CodexTempSchemaFile
{
    private static readonly UTF8Encoding _utf8WithoutBom =
        new(encoderShouldEmitUTF8Identifier: false);

    public static string Create(string jsonSchema_)
    {
        ArgumentNullException.ThrowIfNull(
            jsonSchema_,
            nameof(jsonSchema_));

        string tempPath = Path.Combine(
            Path.GetTempPath(),
            $"codex-schema-{Guid.NewGuid():N}.json");

        File.WriteAllText(
            tempPath,
            jsonSchema_,
            _utf8WithoutBom);

        return tempPath;
    }

    public static void Delete(string? filePath_)
    {
        if (string.IsNullOrEmpty(filePath_))
        {
            return;
        }

        try
        {
            if (File.Exists(filePath_))
            {
                File.Delete(filePath_);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
