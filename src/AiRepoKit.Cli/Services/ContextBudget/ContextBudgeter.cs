using System.Text.Json;
using AiRepoKit.Cli.Models.ContextBudget;

namespace AiRepoKit.Cli.Services.ContextBudget;

public sealed class ContextBudgeter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public BudgetResult<IReadOnlyList<T>> Apply<T>(IEnumerable<T> items_, int? budget_, Func<T, string> path_, Func<T, int> priority_)
    {
        List<T> selected = [];
        List<BudgetCut> cuts = [];
        int estimated = 0;
        int? budget = NormalizeBudget(budget_);

        foreach (T item in items_.OrderByDescending(priority_).ThenBy(path_, StringComparer.OrdinalIgnoreCase))
        {
            int itemTokens = EstimateTokens(item);
            if (budget.HasValue && estimated + itemTokens > budget.Value)
            {
                cuts.Add(new BudgetCut(path_(item), "Budget exceeded; lower-priority item omitted.", itemTokens));
                continue;
            }

            selected.Add(item);
            estimated += itemTokens;
        }

        return new BudgetResult<IReadOnlyList<T>>(selected, estimated, budget, cuts.Count > 0, cuts);
    }

    public BudgetResult<T> Report<T>(T value_, int? budget_, IReadOnlyList<BudgetCut>? cuts_ = null)
    {
        int estimated = EstimateTokens(value_);
        int? budget = NormalizeBudget(budget_);
        IReadOnlyList<BudgetCut> cuts = cuts_ ?? [];
        bool truncated = cuts.Count > 0 || (budget.HasValue && estimated > budget.Value);
        return new BudgetResult<T>(value_, estimated, budget, truncated, cuts);
    }

    public static int EstimateTokens(object? value_)
    {
        if (value_ is null)
        {
            return 0;
        }

        string text = value_ is string stringValue ? stringValue : JsonSerializer.Serialize(value_, JsonOptions);
        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }

    private static int? NormalizeBudget(int? budget_)
    {
        return budget_.HasValue && budget_.Value > 0 ? budget_.Value : null;
    }
}
