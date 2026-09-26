namespace AiRepoKit.Orchestration;

using System.Text.Json.Serialization;

[JsonUnmappedMemberHandlingAttribute(JsonUnmappedMemberHandling.Disallow)]
internal sealed class WorkflowPersistenceRecord
{
    [JsonRequired]
    [JsonPropertyOrder(0)]
    public string SchemaId
    {
        get;
        set;
    } = string.Empty;

    [JsonRequired]
    [JsonPropertyOrder(1)]
    public int SchemaVersion
    {
        get;
        set;
    }

    [JsonRequired]
    [JsonPropertyOrder(2)]
    public string WorkflowId
    {
        get;
        set;
    } = string.Empty;

    [JsonRequired]
    [JsonPropertyOrder(3)]
    public long Revision
    {
        get;
        set;
    }

    [JsonRequired]
    [JsonPropertyOrder(4)]
    public WorkflowPersistenceEventRecord? Event
    {
        get;
        set;
    }

    [JsonRequired]
    [JsonPropertyOrder(5)]
    public WorkflowPersistenceStateRecord? State
    {
        get;
        set;
    }
}

[JsonUnmappedMemberHandlingAttribute(JsonUnmappedMemberHandling.Disallow)]
internal sealed class WorkflowPersistenceEventRecord
{
    [JsonRequired]
    [JsonPropertyOrder(0)]
    public string WorkflowId
    {
        get;
        set;
    } = string.Empty;

    [JsonRequired]
    [JsonPropertyOrder(1)]
    public long Sequence
    {
        get;
        set;
    }

    [JsonRequired]
    [JsonPropertyOrder(2)]
    public int Kind
    {
        get;
        set;
    }

    [JsonRequired]
    [JsonPropertyOrder(3)]
    public string? TaskId
    {
        get;
        set;
    }

    [JsonRequired]
    [JsonPropertyOrder(4)]
    public int? PreviousWorkflowStatus
    {
        get;
        set;
    }

    [JsonRequired]
    [JsonPropertyOrder(5)]
    public int? TargetWorkflowStatus
    {
        get;
        set;
    }

    [JsonRequired]
    [JsonPropertyOrder(6)]
    public int? PreviousStepStatus
    {
        get;
        set;
    }

    [JsonRequired]
    [JsonPropertyOrder(7)]
    public int? TargetStepStatus
    {
        get;
        set;
    }
}

[JsonUnmappedMemberHandlingAttribute(JsonUnmappedMemberHandling.Disallow)]
internal sealed class WorkflowPersistenceStateRecord
{
    [JsonRequired]
    [JsonPropertyOrder(0)]
    public int SourceImplementationPlanRevision
    {
        get;
        set;
    }

    [JsonRequired]
    [JsonPropertyOrder(1)]
    public int Status
    {
        get;
        set;
    }

    [JsonRequired]
    [JsonPropertyOrder(2)]
    public List<WorkflowPersistenceStepRecord?>? Steps
    {
        get;
        set;
    }
}

[JsonUnmappedMemberHandlingAttribute(JsonUnmappedMemberHandling.Disallow)]
internal sealed class WorkflowPersistenceStepRecord
{
    [JsonRequired]
    [JsonPropertyOrder(0)]
    public string TaskId
    {
        get;
        set;
    } = string.Empty;

    [JsonRequired]
    [JsonPropertyOrder(1)]
    public int Status
    {
        get;
        set;
    }
}
