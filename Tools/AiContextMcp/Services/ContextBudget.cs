using System.Text;
using System.Text.Json;
using AiRepo.ContextMcp.Models;

namespace AiRepo.ContextMcp.Services;

public sealed class ContextBudget
{
    public ContextBudget(ContextBudgetOptions options_)
    {
        this.Options = options_;
    }

    public ContextBudgetOptions Options { get; }

    public ToolEnvelope<T> Envelope<T>(T data_, bool redactedOnly_)
    {
        string json = JsonSerializer.Serialize(data_);
        int size = Encoding.UTF8.GetByteCount(json);
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
