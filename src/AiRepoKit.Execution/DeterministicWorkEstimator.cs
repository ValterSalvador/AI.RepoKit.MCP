namespace AiRepoKit.Execution;

public static class DeterministicWorkEstimator
{
    public const string AlgorithmId =
        "ai.repokit.deterministic-work-estimator/v1";

    public const int CharactersPerEstimatedToken =
        4;

    public static int EstimateTokens(
        string text_)
    {
        ArgumentNullException.ThrowIfNull(
            text_,
            nameof(text_));

        if (text_.Length == 0)
        {
            return 0;
        }

        long estimatedTokens =
            (
                (long) text_.Length +
                CharactersPerEstimatedToken -
                1L
            ) /
            CharactersPerEstimatedToken;

        return checked(
            (int) estimatedTokens);
    }

    public static IReadOnlyList<ExecutableTaskEstimate> Estimate(
        ExecutableWork work_)
    {
        ArgumentNullException.ThrowIfNull(
            work_,
            nameof(work_));

        ExecutableTaskEstimate[] estimates =
            new ExecutableTaskEstimate[work_.Tasks.Count];

        for (int taskIndex = 0; taskIndex < work_.Tasks.Count; taskIndex++)
        {
            ExecutableTask task =
                work_.Tasks[taskIndex];

            int estimatedInstructionTokens =
                EstimateTokens(
                    task.Instruction);

            int directPrerequisiteCount =
                0;

            for (
                int dependencyIndex = 0;
                dependencyIndex < work_.Dependencies.Count;
                dependencyIndex++
            )
            {
                if (string.Equals(
                        work_.Dependencies[dependencyIndex].TaskId,
                        task.Id,
                        StringComparison.Ordinal))
                {
                    directPrerequisiteCount++;
                }
            }

            int validationRequirementCount =
                0;

            for (
                int validationIndex = 0;
                validationIndex < work_.ValidationRequirements.Count;
                validationIndex++
            )
            {
                if (string.Equals(
                        work_.ValidationRequirements[validationIndex].TaskId,
                        task.Id,
                        StringComparison.Ordinal))
                {
                    validationRequirementCount++;
                }
            }

            int modelRequiredCapabilityCount =
                0;

            for (
                int modelIndex = 0;
                modelIndex < work_.ModelRequirements.Count;
                modelIndex++
            )
            {
                ModelRequirement requirement =
                    work_.ModelRequirements[modelIndex];

                if (string.Equals(
                        requirement.TaskId,
                        task.Id,
                        StringComparison.Ordinal))
                {
                    modelRequiredCapabilityCount =
                        requirement.RequiredCapabilities.Count;

                    break;
                }
            }

            int agentRequiredCapabilityCount =
                0;

            for (
                int agentIndex = 0;
                agentIndex < work_.AgentRequirements.Count;
                agentIndex++
            )
            {
                AgentRequirement requirement =
                    work_.AgentRequirements[agentIndex];

                if (string.Equals(
                        requirement.TaskId,
                        task.Id,
                        StringComparison.Ordinal))
                {
                    agentRequiredCapabilityCount =
                        requirement.RequiredCapabilities.Count;

                    break;
                }
            }

            int complexityScore =
                checked(
                    estimatedInstructionTokens +
                    directPrerequisiteCount +
                    validationRequirementCount +
                    modelRequiredCapabilityCount +
                    agentRequiredCapabilityCount);

            estimates[taskIndex] =
                new ExecutableTaskEstimate(
                    task.Id,
                    complexityScore,
                    estimatedInstructionTokens);
        }

        return Array.AsReadOnly(
            estimates);
    }
}
