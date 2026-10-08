namespace AiRepoKit.WorkflowExecution;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;

internal sealed class WorkflowCheckpointJournal
{
    public const string SchemaId =
        "ai.repokit.workflow-execution-journal-record";

    public const int CurrentSchemaVersion =
        1;

    private static readonly Regex _recordFileNamePattern =
        new(@"^[0-9]{20}\.json$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions _jsonOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

    private readonly string _recordsDirectory;
    private readonly List<JournalRecord> _records;
    private long _nextSequence;

    public IReadOnlyList<JournalRecord> Records =>
        this._records.AsReadOnly();

    public CheckpointStartedRecord StartedRecord =>
        (CheckpointStartedRecord) this._records[0];

    public ExecutionCompletedRecord? CompletedRecord =>
        this._records.OfType<ExecutionCompletedRecord>().LastOrDefault();

    public ProjectionCompletedRecord? ProjectionCompletedRecord =>
        this._records.OfType<ProjectionCompletedRecord>().LastOrDefault();

    public string RecordsDirectory =>
        this._recordsDirectory;

    public long NextSequence =>
        this._nextSequence;

    private WorkflowCheckpointJournal(
        string recordsDirectory_,
        List<JournalRecord> records_)
    {
        this._recordsDirectory = recordsDirectory_;
        this._records = records_;
        this._nextSequence = records_.Count + 1;
    }

    public static string GetCheckpointDirectory(
        string storageRoot_,
        WorkflowId workflowId_,
        string taskId_,
        long basePersistenceRevision_)
    {
        string workflowIdHex =
            Convert.ToHexString(
                Encoding.UTF8.GetBytes(
                    workflowId_.Value)).ToLowerInvariant();

        string taskIdHex =
            Convert.ToHexString(
                Encoding.UTF8.GetBytes(
                    taskId_)).ToLowerInvariant();

        return Path.Combine(
            storageRoot_,
            "workflows",
            workflowIdHex,
            "execution-checkpoints",
            taskIdHex,
            basePersistenceRevision_.ToString("D20"));
    }

    public static string GetRecordsDirectory(
        string storageRoot_,
        WorkflowId workflowId_,
        string taskId_,
        long basePersistenceRevision_)
    {
        return Path.Combine(
            GetCheckpointDirectory(
                storageRoot_,
                workflowId_,
                taskId_,
                basePersistenceRevision_),
            "records");
    }

    public static IReadOnlyList<long> DiscoverCandidateRevisions(
        string storageRoot_,
        WorkflowId workflowId_,
        string taskId_)
    {
        string workflowIdHex =
            Convert.ToHexString(
                Encoding.UTF8.GetBytes(
                    workflowId_.Value)).ToLowerInvariant();

        string taskIdHex =
            Convert.ToHexString(
                Encoding.UTF8.GetBytes(
                    taskId_)).ToLowerInvariant();

        string checkpointsDir =
            Path.Combine(
                storageRoot_,
                "workflows",
                workflowIdHex,
                "execution-checkpoints",
                taskIdHex);

        if (!Directory.Exists(checkpointsDir))
        {
            return Array.Empty<long>();
        }

        List<long> revisions = [];

        foreach (string dir in Directory.GetDirectories(checkpointsDir))
        {
            string dirName = Path.GetFileName(dir);
            if (dirName.Length == 20 && long.TryParse(dirName, out long revision) && revision > 0)
            {
                revisions.Add(revision);
            }
        }

        revisions.Sort((a, b) => b.CompareTo(a)); // Descending order
        return revisions;
    }

    public static WorkflowCheckpointJournal OpenExisting(
        string storageRoot_,
        WorkflowId workflowId_,
        string taskId_,
        long basePersistenceRevision_)
    {
        string recordsDir =
            GetRecordsDirectory(
                storageRoot_,
                workflowId_,
                taskId_,
                basePersistenceRevision_);

        if (!Directory.Exists(recordsDir))
        {
            throw new DirectoryNotFoundException(
                $"Records directory does not exist: {recordsDir}");
        }

        List<JournalRecord> records = ReadAllRecords(recordsDir);
        return new WorkflowCheckpointJournal(recordsDir, records);
    }

    public static WorkflowCheckpointJournal CreateNew(
        string storageRoot_,
        WorkflowId workflowId_,
        string taskId_,
        long basePersistenceRevision_,
        string baseWorkflowStateFingerprint_,
        string inputFingerprint_,
        string registryFingerprint_)
    {
        if (!Directory.Exists(storageRoot_))
        {
            throw new DirectoryNotFoundException(
                $"Storage root directory does not exist: {storageRoot_}");
        }

        string recordsDir =
            GetRecordsDirectory(
                storageRoot_,
                workflowId_,
                taskId_,
                basePersistenceRevision_);

        if (Directory.Exists(recordsDir) && Directory.GetFiles(recordsDir).Length > 0)
        {
            throw new InvalidOperationException(
                $"Cannot create new journal: records already exist at {recordsDir}");
        }

        Directory.CreateDirectory(recordsDir);

        WorkflowCheckpointJournal journal =
            new(recordsDir, []);

        CheckpointStartedRecord startedRecord =
            new()
            {
                SchemaId = SchemaId,
                SchemaVersion = CurrentSchemaVersion,
                Sequence = 1,
                Kind = JournalRecordKind.CheckpointStarted,
                WorkflowId = workflowId_.Value,
                TaskId = taskId_,
                BasePersistenceRevision = basePersistenceRevision_,
                BaseWorkflowStateFingerprint = baseWorkflowStateFingerprint_,
                InputFingerprint = inputFingerprint_,
                RegistryFingerprint = registryFingerprint_
            };

        journal.AppendRecord(startedRecord);
        return journal;
    }

    public void AppendRecord(JournalRecord record_)
    {
        ArgumentNullException.ThrowIfNull(record_, nameof(record_));

        if (record_.Sequence != this._nextSequence)
        {
            throw new InvalidOperationException(
                $"Record sequence {record_.Sequence} does not match expected sequence {this._nextSequence}.");
        }

        string fileName = $"{record_.Sequence:D20}.json";
        string destinationPath = Path.Combine(this._recordsDirectory, fileName);
        string tempPath = Path.Combine(this._recordsDirectory, $".{fileName}.{Guid.NewGuid():N}.tmp");

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(record_, record_.GetType(), _jsonOptions);

        using (FileStream stream = new(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.SequentialScan))
        {
            stream.Write(payload);
            stream.Flush(flushToDisk: true);
        }

        try
        {
            File.Move(tempPath, destinationPath, overwrite: false);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                $"Journal record append collision or write failure for sequence {record_.Sequence}.",
                exception);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        this._records.Add(record_);
        this._nextSequence++;
    }

    public InvocationOutcomeRecord? FindOutcome(long operationOrdinal_)
    {
        return this._records
            .OfType<InvocationOutcomeRecord>()
            .FirstOrDefault(r => r.OperationOrdinal == operationOrdinal_);
    }

    public InvocationIntentRecord? FindActiveAmbiguousIntent(long operationOrdinal_)
    {
        // An intent is active and ambiguous if there is no outcome for this ordinal,
        // and for the intent's generation there is no ReconciliationNotExecuted.
        if (this.FindOutcome(operationOrdinal_) is not null)
        {
            return null;
        }

        HashSet<int> notExecutedGenerations =
            this._records
                .OfType<ReconciliationNotExecutedRecord>()
                .Where(r => r.OperationOrdinal == operationOrdinal_)
                .Select(r => r.InvocationGeneration)
                .ToHashSet();

        return this._records
            .OfType<InvocationIntentRecord>()
            .Where(r => r.OperationOrdinal == operationOrdinal_ && !notExecutedGenerations.Contains(r.InvocationGeneration))
            .OrderByDescending(r => r.InvocationGeneration)
            .FirstOrDefault();
    }

    public int GetNextGenerationForOperation(long operationOrdinal_)
    {
        int maxGen = 0;
        foreach (JournalRecord rec in this._records)
        {
            if (rec is InvocationIntentRecord intent && intent.OperationOrdinal == operationOrdinal_)
            {
                if (intent.InvocationGeneration > maxGen)
                {
                    maxGen = intent.InvocationGeneration;
                }
            }
            else if (rec is ReconciliationNotExecutedRecord rne && rne.OperationOrdinal == operationOrdinal_)
            {
                if (rne.InvocationGeneration >= maxGen)
                {
                    maxGen = rne.InvocationGeneration + 1;
                }
            }
        }

        return maxGen == 0 ? 1 : maxGen;
    }

    private static List<JournalRecord> ReadAllRecords(string recordsDirectory_)
    {
        string[] files = Directory.GetFiles(recordsDirectory_, "*.json");
        Array.Sort(files, StringComparer.Ordinal);

        List<JournalRecord> records = [];
        long expectedSequence = 1;

        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);
            if (!_recordFileNamePattern.IsMatch(fileName))
            {
                throw new InvalidOperationException(
                    $"Non-canonical journal record file name detected: '{fileName}'.");
            }

            long seqFromName = long.Parse(fileName.Substring(0, 20));
            if (seqFromName != expectedSequence)
            {
                throw new InvalidOperationException(
                    $"Journal record sequence gap or conflict detected. Expected {expectedSequence}, found {seqFromName}.");
            }

            byte[] payload = File.ReadAllBytes(file);
            if (payload.Length >= 3 && payload[0] == 0xEF && payload[1] == 0xBB && payload[2] == 0xBF)
            {
                throw new InvalidOperationException(
                    "Journal record file must not contain a UTF-8 BOM.");
            }

            JournalRecord record;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(payload);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("schemaId", out JsonElement schemaIdElem) ||
                    !string.Equals(schemaIdElem.GetString(), SchemaId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Unknown or invalid journal schema ID.");
                }

                if (!root.TryGetProperty("schemaVersion", out JsonElement schemaVerElem) ||
                    schemaVerElem.GetInt32() != CurrentSchemaVersion)
                {
                    throw new InvalidOperationException("Unsupported journal schema version.");
                }

                if (!root.TryGetProperty("kind", out JsonElement kindElem))
                {
                    throw new InvalidOperationException("Missing journal record kind.");
                }

                string? kindString = kindElem.GetString();
                record = kindString switch
                {
                    nameof(JournalRecordKind.CheckpointStarted) =>
                        JsonSerializer.Deserialize<CheckpointStartedRecord>(payload, _jsonOptions)!,
                    nameof(JournalRecordKind.InvocationIntent) =>
                        JsonSerializer.Deserialize<InvocationIntentRecord>(payload, _jsonOptions)!,
                    nameof(JournalRecordKind.InvocationOutcome) =>
                        JsonSerializer.Deserialize<InvocationOutcomeRecord>(payload, _jsonOptions)!,
                    nameof(JournalRecordKind.ReconciliationNotExecuted) =>
                        JsonSerializer.Deserialize<ReconciliationNotExecutedRecord>(payload, _jsonOptions)!,
                    nameof(JournalRecordKind.ExecutionCompleted) =>
                        JsonSerializer.Deserialize<ExecutionCompletedRecord>(payload, _jsonOptions)!,
                    nameof(JournalRecordKind.ProjectionCompleted) =>
                        JsonSerializer.Deserialize<ProjectionCompletedRecord>(payload, _jsonOptions)!,
                    _ => throw new InvalidOperationException($"Unknown journal record kind: '{kindString}'.")
                };
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Corrupt or invalid journal record at '{file}'.", ex);
            }

            if (record.Sequence != expectedSequence)
            {
                throw new InvalidOperationException(
                    $"Record internal sequence {record.Sequence} does not match expected sequence {expectedSequence}.");
            }

            if (expectedSequence == 1 && record.Kind != JournalRecordKind.CheckpointStarted)
            {
                throw new InvalidOperationException(
                    "First journal record must be CheckpointStarted.");
            }

            records.Add(record);
            expectedSequence++;
        }

        if (records.Count == 0)
        {
            throw new InvalidOperationException(
                "Journal records directory is empty.");
        }

        return records;
    }
}

internal enum JournalRecordKind
{
    CheckpointStarted,
    InvocationIntent,
    InvocationOutcome,
    ReconciliationNotExecuted,
    ExecutionCompleted,
    ProjectionCompleted
}

internal abstract class JournalRecord
{
    [JsonPropertyOrder(0)]
    public string SchemaId { get; set; } = WorkflowCheckpointJournal.SchemaId;

    [JsonPropertyOrder(1)]
    public int SchemaVersion { get; set; } = WorkflowCheckpointJournal.CurrentSchemaVersion;

    [JsonPropertyOrder(2)]
    public long Sequence { get; set; }

    [JsonPropertyOrder(3)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public JournalRecordKind Kind { get; set; }
}

internal sealed class CheckpointStartedRecord : JournalRecord
{
    public CheckpointStartedRecord()
    {
        this.Kind = JournalRecordKind.CheckpointStarted;
    }

    [JsonPropertyOrder(4)]
    public string WorkflowId { get; set; } = string.Empty;

    [JsonPropertyOrder(5)]
    public string TaskId { get; set; } = string.Empty;

    [JsonPropertyOrder(6)]
    public long BasePersistenceRevision { get; set; }

    [JsonPropertyOrder(7)]
    public string BaseWorkflowStateFingerprint { get; set; } = string.Empty;

    [JsonPropertyOrder(8)]
    public string InputFingerprint { get; set; } = string.Empty;

    [JsonPropertyOrder(9)]
    public string RegistryFingerprint { get; set; } = string.Empty;
}

internal sealed class InvocationIntentRecord : JournalRecord
{
    public InvocationIntentRecord()
    {
        this.Kind = JournalRecordKind.InvocationIntent;
    }

    [JsonPropertyOrder(4)]
    public long OperationOrdinal { get; set; }

    [JsonPropertyOrder(5)]
    public string OperationId { get; set; } = string.Empty;

    [JsonPropertyOrder(6)]
    public string OperationKind { get; set; } = string.Empty;

    [JsonPropertyOrder(7)]
    public int InvocationGeneration { get; set; }

    [JsonPropertyOrder(8)]
    public string ExternalIdentity { get; set; } = string.Empty;

    [JsonPropertyOrder(9)]
    public string RequestFingerprint { get; set; } = string.Empty;
}

internal sealed class InvocationOutcomeRecord : JournalRecord
{
    public InvocationOutcomeRecord()
    {
        this.Kind = JournalRecordKind.InvocationOutcome;
    }

    [JsonPropertyOrder(4)]
    public long OperationOrdinal { get; set; }

    [JsonPropertyOrder(5)]
    public string OperationId { get; set; } = string.Empty;

    [JsonPropertyOrder(6)]
    public string OperationKind { get; set; } = string.Empty;

    [JsonPropertyOrder(7)]
    public int InvocationGeneration { get; set; }

    [JsonPropertyOrder(8)]
    public string RequestFingerprint { get; set; } = string.Empty;

    [JsonPropertyOrder(9)]
    public bool IsNullReturn { get; set; }

    [JsonPropertyOrder(10)]
    public int? AgentStatus { get; set; }

    [JsonPropertyOrder(11)]
    public string? AgentOutputText { get; set; }

    [JsonPropertyOrder(12)]
    public string? AgentDiagnosticText { get; set; }

    [JsonPropertyOrder(13)]
    public string? AgentSessionReference { get; set; }

    [JsonPropertyOrder(14)]
    public bool? ValidationPassed { get; set; }

    [JsonPropertyOrder(15)]
    public string? ValidationEvidence { get; set; }
}

internal sealed class ReconciliationNotExecutedRecord : JournalRecord
{
    public ReconciliationNotExecutedRecord()
    {
        this.Kind = JournalRecordKind.ReconciliationNotExecuted;
    }

    [JsonPropertyOrder(4)]
    public long OperationOrdinal { get; set; }

    [JsonPropertyOrder(5)]
    public string OperationId { get; set; } = string.Empty;

    [JsonPropertyOrder(6)]
    public int InvocationGeneration { get; set; }
}

internal sealed class ExecutionCompletedRecord : JournalRecord
{
    public ExecutionCompletedRecord()
    {
        this.Kind = JournalRecordKind.ExecutionCompleted;
    }

    [JsonPropertyOrder(4)]
    public string FinalStateFingerprint { get; set; } = string.Empty;

    [JsonPropertyOrder(5)]
    public int AttemptCount { get; set; }

    [JsonPropertyOrder(6)]
    public bool BudgetExhausted { get; set; }

    [JsonPropertyOrder(7)]
    public int ExpectedProjectionTransitionCount { get; set; }
}

internal sealed class ProjectionCompletedRecord : JournalRecord
{
    public ProjectionCompletedRecord()
    {
        this.Kind = JournalRecordKind.ProjectionCompleted;
    }

    [JsonPropertyOrder(4)]
    public long FinalPersistenceRevision { get; set; }
}
