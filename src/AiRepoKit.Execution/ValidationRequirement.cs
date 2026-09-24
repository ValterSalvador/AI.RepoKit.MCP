namespace AiRepoKit.Execution;

public sealed record ValidationRequirement
{
    public string Id
    {
        get;
    }

    public string TaskId
    {
        get;
    }

    public string SourceAcceptanceCriterionId
    {
        get;
    }

    public ValidationStrategy Strategy
    {
        get;
    }

    public string Statement
    {
        get;
    }

    public ValidationRequirement(
        string id_,
        string taskId_,
        string sourceAcceptanceCriterionId_,
        ValidationStrategy strategy_,
        string statement_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            id_,
            nameof(id_));

        ArgumentException.ThrowIfNullOrWhiteSpace(
            taskId_,
            nameof(taskId_));

        ArgumentException.ThrowIfNullOrWhiteSpace(
            sourceAcceptanceCriterionId_,
            nameof(sourceAcceptanceCriterionId_));

        ArgumentException.ThrowIfNullOrWhiteSpace(
            statement_,
            nameof(statement_));

        if (
            strategy_ != ValidationStrategy.Build &&
            strategy_ != ValidationStrategy.Test &&
            strategy_ != ValidationStrategy.Policy
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(strategy_),
                strategy_,
                "Validation strategy must be Build, Test or Policy.");
        }

        this.Id =
            id_;
        this.TaskId =
            taskId_;
        this.SourceAcceptanceCriterionId =
            sourceAcceptanceCriterionId_;
        this.Strategy =
            strategy_;
        this.Statement =
            statement_;
    }
}