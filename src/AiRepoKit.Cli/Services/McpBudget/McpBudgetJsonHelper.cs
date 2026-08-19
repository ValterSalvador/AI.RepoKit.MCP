using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiRepoKit.Cli.Services.McpBudget;

/// <summary>
/// Static helpers for MCP budget JSON/envelope inspection, secret detection,
/// and redaction marker testing. Ported from MeasureMcpResponseBudget.ps1.
/// </summary>
internal static class McpBudgetJsonHelper
{
    // Seven secret exposure patterns from the PowerShell reference implementation.
    private static readonly Regex[] SecretPatterns =
    [
        new(@"(?i)\bpassword\s*[=:]\s*[^;\s,""'{}[\]]+", RegexOptions.Compiled),
        new(@"(?i)\bsecret\s*=\s*[^;\s,""'{}[\]]+", RegexOptions.Compiled),
        new(@"(?i)\btoken\s*=\s*[^;\s,""'{}[\]]+", RegexOptions.Compiled),
        new(@"(?i)\bclientSecret\s*[=:]\s*[^;\s,""'{}[\]]+", RegexOptions.Compiled),
        new(@"(?i)\bprivateKey\s*[=:]\s*[^;\s,""'{}[\]]+", RegexOptions.Compiled),
        new(@"(?i)\bconnectionString\s*[=:]\s*[^;\n\r,""'{}[\]]+", RegexOptions.Compiled),
        new(@"(?i)\bUser\s+Id\s*=\s*[^;]+;\s*Password\s*=\s*[^;]+", RegexOptions.Compiled),
    ];

    private static readonly Regex RedactionMarkerPattern =
        new(@"(?i)<redacted>|redacted|\*\*\*", RegexOptions.Compiled);

    /// <summary>
    /// Tests whether the text matches any of the seven secret exposure patterns.
    /// Case-insensitive. Returns true if a potential secret value is detected.
    /// </summary>
    public static bool TestSecretExposure(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (Regex pattern in SecretPatterns)
        {
            if (pattern.IsMatch(text)) return true;
        }
        return false;
    }

    /// <summary>
    /// Tests whether the text contains a redaction marker token.
    /// </summary>
    public static bool TestRedactionMarker(string text) =>
        !string.IsNullOrEmpty(text) && RedactionMarkerPattern.IsMatch(text);

    /// <summary>
    /// Returns the UTF-8 byte count of the string. This is used to calculate SizeBytes
    /// from the raw JSON-RPC response line — not from re-serialized JSON.
    /// </summary>
    public static int GetUtf8ByteCount(string value) =>
        string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value);

    /// <summary>
    /// Recursive case-insensitive property search through a JsonElement (objects and arrays).
    /// Returns the first matching value element, or null if not found.
    /// Matches the behavior of Find-PropertyValue from the PowerShell reference.
    /// </summary>
    public static JsonElement? FindPropertyValue(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value;
                }

                JsonElement? nested = FindPropertyValue(property.Value, name);
                if (nested.HasValue) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                JsonElement? found = FindPropertyValue(item, name);
                if (found.HasValue) return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the "envelope source" from a tool call response as a JSON string.
    /// Priority matches Get-ToolEnvelopeSource from the PowerShell reference:
    ///   1. result.structuredContent
    ///   2. result.content[].text → parsed as JSON if valid, else the content item
    ///   3. Recursively found structuredContent
    ///   4. Recursively found content/text → parsed as JSON if valid, else item
    ///   5. Full response as fallback
    /// </summary>
    public static string GetToolEnvelopeSourceJson(JsonElement response)
    {
        // Priority 1 & 2: search result directly
        if (response.TryGetProperty("result", out JsonElement result))
        {
            if (result.TryGetProperty("structuredContent", out JsonElement sc) &&
                sc.ValueKind != JsonValueKind.Null)
            {
                return sc.GetRawText();
            }

            if (result.TryGetProperty("content", out JsonElement content) &&
                content.ValueKind == JsonValueKind.Array)
            {
                string? fromContent = TryExtractFromContentArray(content);
                if (fromContent is not null) return fromContent;
            }
        }

        // Priority 3: recursive structuredContent search
        JsonElement? foundSc = FindPropertyValue(response, "structuredContent");
        if (foundSc.HasValue && foundSc.Value.ValueKind != JsonValueKind.Null)
        {
            return foundSc.Value.GetRawText();
        }

        // Priority 4: recursive content/text search
        JsonElement? foundContent = FindPropertyValue(response, "content");
        if (foundContent.HasValue && foundContent.Value.ValueKind == JsonValueKind.Array)
        {
            string? fromContent = TryExtractFromContentArray(foundContent.Value, recursiveTextLookup: true);
            if (fromContent is not null) return fromContent;
        }

        // Priority 5: full response fallback
        return response.GetRawText();
    }

    private static string? TryExtractFromContentArray(
        JsonElement contentArray,
        bool recursiveTextLookup = false)
    {
        foreach (JsonElement item in contentArray.EnumerateArray())
        {
            JsonElement? textElement = recursiveTextLookup
                ? FindPropertyValue(item, "text")
                : item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("text", out JsonElement directText)
                        ? directText
                        : null;

            if (!textElement.HasValue ||
                textElement.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? text = textElement.Value.GetString();
            if (string.IsNullOrWhiteSpace(text)) continue;

            // Try to parse text as JSON and return raw text of parsed document.
            // On failure, return the raw text of the content item itself.
            try
            {
                using JsonDocument parsed = JsonDocument.Parse(text);
                string rawText = parsed.RootElement.GetRawText();
                return rawText;
            }
            catch
            {
                return item.GetRawText();
            }
        }

        return null;
    }
}
