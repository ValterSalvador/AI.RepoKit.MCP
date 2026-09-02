namespace AiRepoKit.Spec.Persistence;

public sealed record SpecWorkspaceSnapshot
{
    internal SpecWorkspaceSnapshot(
        RequirementSet? requirementSet_,
        WorkSpec? workSpec_,
        ImplementationPlan? implementationPlan_,
        bool isWorkSpecStale_,
        bool isImplementationPlanStale_)
    {
        this.RequirementSet =
            requirementSet_;
        this.WorkSpec =
            workSpec_;
        this.ImplementationPlan =
            implementationPlan_;
        this.IsWorkSpecStale =
            isWorkSpecStale_;
        this.IsImplementationPlanStale =
            isImplementationPlanStale_;
    }

    public RequirementSet? RequirementSet { get; }

    public WorkSpec? WorkSpec { get; }

    public ImplementationPlan? ImplementationPlan { get; }

    public bool IsEmpty =>
        this.RequirementSet is null &&
        this.WorkSpec is null &&
        this.ImplementationPlan is null;

    public bool IsWorkSpecStale { get; }

    public bool IsImplementationPlanStale { get; }
}
