namespace AiRepoKit.Orchestration;

using System.Text.Json;

internal static class WorkflowPersistenceJson
{
    private static readonly JsonSerializerOptions _options =
        new()
        {
            PropertyNamingPolicy =
                JsonNamingPolicy.CamelCase,
            WriteIndented =
                false
        };

    public static byte[] Serialize(
        WorkflowId workflowId_,
        long revision_,
        WorkflowExecutionEvent event_,
        WorkflowState state_)
    {
        ArgumentNullException.ThrowIfNull(
            workflowId_,
            nameof(workflowId_));
        ArgumentNullException.ThrowIfNull(
            event_,
            nameof(event_));
        ArgumentNullException.ThrowIfNull(
            state_,
            nameof(state_));

        WorkflowPersistenceRecord record =
            new()
            {
                SchemaId =
                    WorkflowPersistenceStore.SchemaId,
                SchemaVersion =
                    WorkflowPersistenceStore.CurrentSchemaVersion,
                WorkflowId =
                    workflowId_.Value,
                Revision =
                    revision_,
                Event =
                    ToRecord(
                        event_),
                State =
                    ToRecord(
                        state_)
            };

        return JsonSerializer.SerializeToUtf8Bytes(
            record,
            _options);
    }

    public static WorkflowPersistenceSnapshot Deserialize(
        byte[] payload_,
        WorkflowId expectedWorkflowId_)
    {
        ArgumentNullException.ThrowIfNull(
            payload_,
            nameof(payload_));
        ArgumentNullException.ThrowIfNull(
            expectedWorkflowId_,
            nameof(expectedWorkflowId_));

        if (payload_.Length >= 3 &&
            payload_[0] == 0xEF &&
            payload_[1] == 0xBB &&
            payload_[2] == 0xBF)
        {
            throw new InvalidDataException(
                "Canonical workflow persistence records must not contain a UTF-8 BOM.");
        }

        try
        {
            WorkflowPersistenceRecord? record =
                JsonSerializer.Deserialize<WorkflowPersistenceRecord>(
                    payload_,
                    _options);

            if (record is null)
            {
                throw new InvalidDataException(
                    "Workflow persistence record is empty.");
            }

            return ToSnapshot(
                record,
                expectedWorkflowId_);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or
            NotSupportedException or
            ArgumentException or
            InvalidOperationException)
        {
            throw new InvalidDataException(
                "Workflow persistence record is invalid.",
                exception);
        }
    }

    private static WorkflowPersistenceEventRecord ToRecord(
        WorkflowExecutionEvent event_)
    {
        return new WorkflowPersistenceEventRecord
        {
            WorkflowId =
                event_.WorkflowId.Value,
            Sequence =
                event_.Sequence,
            Kind =
                (int) event_.Kind,
            TaskId =
                event_.TaskId,
            PreviousWorkflowStatus =
                event_.PreviousWorkflowStatus is null
                    ? null
                    : (int) event_.PreviousWorkflowStatus.Value,
            TargetWorkflowStatus =
                event_.TargetWorkflowStatus is null
                    ? null
                    : (int) event_.TargetWorkflowStatus.Value,
            PreviousStepStatus =
                event_.PreviousStepStatus is null
                    ? null
                    : (int) event_.PreviousStepStatus.Value,
            TargetStepStatus =
                event_.TargetStepStatus is null
                    ? null
                    : (int) event_.TargetStepStatus.Value
        };
    }

    private static WorkflowPersistenceStateRecord ToRecord(
        WorkflowState state_)
    {
        List<WorkflowPersistenceStepRecord?> steps =
            new(
                state_.Steps.Count);

        for (int index = 0; index < state_.Steps.Count; index++)
        {
            steps.Add(
                new WorkflowPersistenceStepRecord
                {
                    TaskId =
                        state_.Steps[index].TaskId,
                    Status =
                        (int) state_.Steps[index].Status
                });
        }

        return new WorkflowPersistenceStateRecord
        {
            SourceImplementationPlanRevision =
                state_.SourceImplementationPlanRevision,
            Status =
                (int) state_.Status,
            Steps =
                steps
        };
    }

    private static WorkflowPersistenceSnapshot ToSnapshot(
        WorkflowPersistenceRecord record_,
        WorkflowId expectedWorkflowId_)
    {
        if (!string.Equals(
                record_.SchemaId,
                WorkflowPersistenceStore.SchemaId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Workflow persistence schema ID is not supported.");
        }

        if (record_.SchemaVersion !=
            WorkflowPersistenceStore.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                "Workflow persistence schema version is not supported.");
        }

        WorkflowId recordWorkflowId =
            new(
                record_.WorkflowId);

        if (!recordWorkflowId.Equals(
                expectedWorkflowId_))
        {
            throw new InvalidDataException(
                "Workflow persistence record belongs to a different workflow.");
        }

        if (record_.Revision <= 0)
        {
            throw new InvalidDataException(
                "Workflow persistence revision must be greater than zero.");
        }

        if (record_.Event is null)
        {
            throw new InvalidDataException(
                "Workflow persistence event is required.");
        }

        if (record_.State is null)
        {
            throw new InvalidDataException(
                "Workflow persistence state is required.");
        }

        WorkflowExecutionEvent eventValue =
            ToEvent(
                record_.Event,
                expectedWorkflowId_);

        WorkflowState state =
            ToState(
                record_.State);

        return new WorkflowPersistenceSnapshot(
            expectedWorkflowId_,
            record_.Revision,
            state,
            eventValue);
    }

    private static WorkflowExecutionEvent ToEvent(
        WorkflowPersistenceEventRecord record_,
        WorkflowId expectedWorkflowId_)
    {
        WorkflowId eventWorkflowId =
            new(
                record_.WorkflowId);

        if (!eventWorkflowId.Equals(
                expectedWorkflowId_))
        {
            throw new InvalidDataException(
                "Workflow persistence event belongs to a different workflow.");
        }

        if (!Enum.IsDefined(
                typeof(WorkflowExecutionEventKind),
                record_.Kind))
        {
            throw new InvalidDataException(
                "Workflow persistence event kind is invalid.");
        }

        WorkflowStatus? previousWorkflowStatus =
            ToWorkflowStatus(
                record_.PreviousWorkflowStatus);

        WorkflowStatus? targetWorkflowStatus =
            ToWorkflowStatus(
                record_.TargetWorkflowStatus);

        WorkflowStepStatus? previousStepStatus =
            ToWorkflowStepStatus(
                record_.PreviousStepStatus);

        WorkflowStepStatus? targetStepStatus =
            ToWorkflowStepStatus(
                record_.TargetStepStatus);

        return new WorkflowExecutionEvent(
            expectedWorkflowId_,
            record_.Sequence,
            (WorkflowExecutionEventKind) record_.Kind,
            record_.TaskId,
            previousWorkflowStatus,
            targetWorkflowStatus,
            previousStepStatus,
            targetStepStatus);
    }

    private static WorkflowState ToState(
        WorkflowPersistenceStateRecord record_)
    {
        if (!Enum.IsDefined(
                typeof(WorkflowStatus),
                record_.Status))
        {
            throw new InvalidDataException(
                "Persisted workflow status is invalid.");
        }

        if (record_.Steps is null)
        {
            throw new InvalidDataException(
                "Persisted workflow step collection is required.");
        }

        WorkflowStepState[] steps =
            new WorkflowStepState[record_.Steps.Count];

        for (int index = 0; index < record_.Steps.Count; index++)
        {
            WorkflowPersistenceStepRecord? step =
                record_.Steps[index];

            if (step is null)
            {
                throw new InvalidDataException(
                    "Persisted workflow step collection contains a null element.");
            }

            if (!Enum.IsDefined(
                    typeof(WorkflowStepStatus),
                    step.Status))
            {
                throw new InvalidDataException(
                    "Persisted workflow step status is invalid.");
            }

            steps[index] =
                new WorkflowStepState(
                    step.TaskId,
                    (WorkflowStepStatus) step.Status);
        }

        return new WorkflowState(
            record_.SourceImplementationPlanRevision,
            (WorkflowStatus) record_.Status,
            steps);
    }

    private static WorkflowStatus? ToWorkflowStatus(
        int? value_)
    {
        if (value_ is null)
        {
            return null;
        }

        if (!Enum.IsDefined(
                typeof(WorkflowStatus),
                value_.Value))
        {
            throw new InvalidDataException(
                "Persisted workflow event status is invalid.");
        }

        return (WorkflowStatus) value_.Value;
    }

    private static WorkflowStepStatus? ToWorkflowStepStatus(
        int? value_)
    {
        if (value_ is null)
        {
            return null;
        }

        if (!Enum.IsDefined(
                typeof(WorkflowStepStatus),
                value_.Value))
        {
            throw new InvalidDataException(
                "Persisted workflow event step status is invalid.");
        }

        return (WorkflowStepStatus) value_.Value;
    }
}
