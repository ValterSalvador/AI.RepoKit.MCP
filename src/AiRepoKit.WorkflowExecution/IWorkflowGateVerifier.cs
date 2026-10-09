namespace AiRepoKit.WorkflowExecution;

public interface IWorkflowGateVerifier
{
    Task<WorkflowGateVerificationResult> VerifyAsync(
        WorkflowGateChallenge challenge_,
        WorkflowGateProof proof_,
        CancellationToken cancellationToken_ = default);
}
