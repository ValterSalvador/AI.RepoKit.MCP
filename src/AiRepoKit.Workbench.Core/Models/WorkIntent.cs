namespace AiRepoKit.Workbench.Core.Models;

/// <summary>
/// Represents the recognized work intents for the workbench routing engine.
/// </summary>
public enum WorkIntent
{
    Plan,
    Fix,
    Do,
    Agentic,
    Review,
    Qa,
    Status
}

/// <summary>
/// Helper methods for work intent parsing and representation.
/// </summary>
public static class WorkIntentParser
{
    public static bool TryParse(string? text_, out WorkIntent intent_)
    {
        if (string.IsNullOrWhiteSpace(text_))
        {
            intent_ = default;
            return false;
        }

        string normalized = text_.Trim();
        if (string.Equals(normalized, "plan", StringComparison.OrdinalIgnoreCase))
        {
            intent_ = WorkIntent.Plan;
            return true;
        }

        if (string.Equals(normalized, "fix", StringComparison.OrdinalIgnoreCase))
        {
            intent_ = WorkIntent.Fix;
            return true;
        }

        if (string.Equals(normalized, "do", StringComparison.OrdinalIgnoreCase))
        {
            intent_ = WorkIntent.Do;
            return true;
        }

        if (string.Equals(normalized, "agentic", StringComparison.OrdinalIgnoreCase))
        {
            intent_ = WorkIntent.Agentic;
            return true;
        }

        if (string.Equals(normalized, "review", StringComparison.OrdinalIgnoreCase))
        {
            intent_ = WorkIntent.Review;
            return true;
        }

        if (string.Equals(normalized, "qa", StringComparison.OrdinalIgnoreCase))
        {
            intent_ = WorkIntent.Qa;
            return true;
        }

        if (string.Equals(normalized, "status", StringComparison.OrdinalIgnoreCase))
        {
            intent_ = WorkIntent.Status;
            return true;
        }

        intent_ = default;
        return false;
    }
}
