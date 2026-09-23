namespace AiRepoKit.Agents.Runtime;

public interface IModelExecutionFailureClassifier
{
    bool IsRetryable(
        ModelRouteCandidate candidate_,
        Exception exception_);
}
