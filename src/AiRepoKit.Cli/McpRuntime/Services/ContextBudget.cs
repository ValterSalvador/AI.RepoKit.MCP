using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.McpRuntime.Models;

namespace AiRepoKit.Cli.McpRuntime.Services;

public sealed class ContextBudget
{
    public ContextBudget(ContextBudgetOptions options_)
    {
        this.Options = options_;
    }

    public ContextBudgetOptions Options { get; }

    public object Envelope<T>(T data_, bool redactedOnly_)
    {
        string json = JsonSerializer.Serialize(data_);
        int size = Encoding.UTF8.GetByteCount(json);
        if (size > this.Options.CombinedBytes)
        {
            return ToolError.Create(
                "BUDGET_EXCEEDED",
                "MCP response exceeded the configured combined response budget.",
                string.Empty,
                true,
                new { estimatedSizeBytes = size, budgetBytes = this.Options.CombinedBytes });
        }

        string hint = size <= this.Options.CompactBytes ? "compact" : size <= this.Options.FullBytes ? "full" : "high";
        return new ToolEnvelope<T>(data_, size, hint, false, false, redactedOnly_);
    }

    public string Trim(string value_, ContextDetail detail_)
    {
        int budget = detail_ switch
        {
            ContextDetail.Brief => this.Options.CompactBytes / 2,
            ContextDetail.Compact => this.Options.CompactBytes,
            _ => this.Options.FullBytes
        };
        if (Encoding.UTF8.GetByteCount(value_) <= budget)
        {
            return value_;
        }

        return value_[..Math.Min(value_.Length, budget)];
    }
}
