namespace AiRepoKit.Execution;

public sealed record ExecutableTask
{
    public string Id
    {
        get;
    }

    public string SourcePlanStepId
    {
        get;
    }

    public string Instruction
    {
        get;
    }

    public ExecutableTask(
        string id_,
        string sourcePlanStepId_,
        string instruction_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            id_,
            nameof(id_));

        ArgumentException.ThrowIfNullOrWhiteSpace(
            sourcePlanStepId_,
            nameof(sourcePlanStepId_));

        ArgumentException.ThrowIfNullOrWhiteSpace(
            instruction_,
            nameof(instruction_));

        this.Id =
            id_;
        this.SourcePlanStepId =
            sourcePlanStepId_;
        this.Instruction =
            instruction_;
    }
}
