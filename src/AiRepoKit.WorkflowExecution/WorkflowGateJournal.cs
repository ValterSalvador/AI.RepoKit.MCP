namespace AiRepoKit.WorkflowExecution;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AiRepoKit.Agents;
using AiRepoKit.Orchestration;

internal sealed class WorkflowGateJournal
{
    public const string SchemaId =
        "ai.repokit.workflow-gate-journal-record";

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
    private readonly List<WorkflowGateJournalRecord> _records;
    private long _nextSequence;

    public IReadOnlyList<WorkflowGateJournalRecord> Records =>
        this._records.AsReadOnly();

    public ChallengeCreatedRecord ChallengeRecord =>
        (ChallengeCreatedRecord) this._records[0];

    public AuthorizationGrantedRecord? GrantedRecord =>
        this._records.OfType<AuthorizationGrantedRecord>().FirstOrDefault();

    public AuthorizationDeniedRecord? DeniedRecord =>
        this._records.OfType<AuthorizationDeniedRecord>().FirstOrDefault();

    public string RecordsDirectory =>
        this._recordsDirectory;

    public long NextSequence =>
        this._nextSequence;

    private WorkflowGateJournal(
        string recordsDirectory_,
        List<WorkflowGateJournalRecord> records_)
    {
        this._recordsDirectory = recordsDirectory_;
        this._records = records_;
        this._nextSequence = records_.Count + 1;
    }

    public static string GetGateDirectory(
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
            "human-gates",
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
            GetGateDirectory(
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
        ArgumentNullException.ThrowIfNull(storageRoot_, nameof(storageRoot_));
        ArgumentNullException.ThrowIfNull(workflowId_, nameof(workflowId_));
        ArgumentNullException.ThrowIfNull(taskId_, nameof(taskId_));

        string workflowIdHex =
            Convert.ToHexString(
                Encoding.UTF8.GetBytes(
                    workflowId_.Value)).ToLowerInvariant();

        string taskIdHex =
            Convert.ToHexString(
                Encoding.UTF8.GetBytes(
                    taskId_)).ToLowerInvariant();

        string gatesDir =
            Path.Combine(
                storageRoot_,
                "workflows",
                workflowIdHex,
                "human-gates",
                taskIdHex);

        if (!Directory.Exists(gatesDir))
        {
            return Array.Empty<long>();
        }

        List<long> revisions = [];

        foreach (string dir in Directory.GetDirectories(gatesDir))
        {
            string dirName = Path.GetFileName(dir);
            if (dirName.Length == 20 && long.TryParse(dirName, out long revision) && revision > 0)
            {
                string recordsDir = Path.Combine(dir, "records");
                if (Directory.Exists(recordsDir) && Directory.GetFiles(recordsDir, "*.json").Length > 0)
                {
                    revisions.Add(revision);
                }
            }
        }

        revisions.Sort((a, b) => b.CompareTo(a)); // Descending order
        return revisions;
    }

    public static WorkflowGateJournal OpenExisting(
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

        List<WorkflowGateJournalRecord> records = ReadAllRecords(recordsDir);
        return new WorkflowGateJournal(recordsDir, records);
    }

    public static WorkflowGateJournal CreateNew(
        string storageRoot_,
        WorkflowId workflowId_,
        string taskId_,
        long basePersistenceRevision_,
        WorkflowGateChallenge challenge_)
    {
        ArgumentNullException.ThrowIfNull(storageRoot_, nameof(storageRoot_));
        ArgumentNullException.ThrowIfNull(workflowId_, nameof(workflowId_));
        ArgumentNullException.ThrowIfNull(taskId_, nameof(taskId_));
        ArgumentNullException.ThrowIfNull(challenge_, nameof(challenge_));

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
                $"Cannot create new human gate journal: records already exist at {recordsDir}");
        }

        Directory.CreateDirectory(recordsDir);

        WorkflowGateJournal journal =
            new(recordsDir, []);

        ChallengeCreatedRecord record =
            new()
            {
                SchemaId = SchemaId,
                SchemaVersion = CurrentSchemaVersion,
                Sequence = 1,
                Kind = WorkflowGateJournalRecordKind.ChallengeCreated,
                WorkflowId = workflowId_.Value,
                TaskId = taskId_,
                GateId = challenge_.GateId,
                BasePersistenceRevision = basePersistenceRevision_,
                GateOrdinal = challenge_.GateOrdinal,
                RequiredPermission = (int) challenge_.RequiredPermission,
                BaseStateFingerprint = challenge_.BaseStateFingerprint,
                InputFingerprint = challenge_.InputFingerprint,
                RegistryFingerprint = challenge_.RegistryFingerprint,
                PolicyFingerprint = challenge_.PolicyFingerprint
            };

        journal.AppendRecord(record);
        return journal;
    }

    public void AppendRecord(WorkflowGateJournalRecord record_)
    {
        ArgumentNullException.ThrowIfNull(record_, nameof(record_));

        if (this._records.Count >= 2)
        {
            throw new InvalidOperationException(
                "Maximum 2 records permitted per human gate journal.");
        }

        if (record_.Sequence != this._nextSequence)
        {
            throw new InvalidOperationException(
                $"Record sequence {record_.Sequence} does not match expected sequence {this._nextSequence}.");
        }

        if (record_.Sequence == 1 && record_.Kind != WorkflowGateJournalRecordKind.ChallengeCreated)
        {
            throw new InvalidOperationException(
                "First journal record must be ChallengeCreated.");
        }

        if (record_.Sequence == 2 &&
            record_.Kind != WorkflowGateJournalRecordKind.AuthorizationGranted &&
            record_.Kind != WorkflowGateJournalRecordKind.AuthorizationDenied)
        {
            throw new InvalidOperationException(
                "Second journal record must be AuthorizationGranted or AuthorizationDenied.");
        }

        string fileName = $"{record_.Sequence:D20}.json";
        string destinationPath = Path.Combine(this._recordsDirectory, fileName);
        string tempPath = Path.Combine(this._recordsDirectory, $".{fileName}.tmp");

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(record_, record_.GetType(), _jsonOptions);

        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

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

    private static List<WorkflowGateJournalRecord> ReadAllRecords(string recordsDirectory_)
    {
        string[] files = Directory.GetFiles(recordsDirectory_, "*.json");
        Array.Sort(files, StringComparer.Ordinal);

        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                "Journal records directory is empty.");
        }

        if (files.Length > 2)
        {
            throw new InvalidOperationException(
                "Journal records count exceeds the maximum limit of 2 records.");
        }

        List<WorkflowGateJournalRecord> records = [];
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

            WorkflowGateJournalRecord record;
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
                    nameof(WorkflowGateJournalRecordKind.ChallengeCreated) =>
                        JsonSerializer.Deserialize<ChallengeCreatedRecord>(payload, _jsonOptions)!,
                    nameof(WorkflowGateJournalRecordKind.AuthorizationGranted) =>
                        JsonSerializer.Deserialize<AuthorizationGrantedRecord>(payload, _jsonOptions)!,
                    nameof(WorkflowGateJournalRecordKind.AuthorizationDenied) =>
                        JsonSerializer.Deserialize<AuthorizationDeniedRecord>(payload, _jsonOptions)!,
                    _ => throw new InvalidOperationException($"Unknown journal record kind: '{kindString}'.")
                };
            }
            catch (Exception exception) when (exception is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Corrupt or invalid journal record at '{file}'.", exception);
            }

            if (record.Sequence != expectedSequence)
            {
                throw new InvalidOperationException(
                    $"Record internal sequence {record.Sequence} does not match expected sequence {expectedSequence}.");
            }

            if (expectedSequence == 1 && record.Kind != WorkflowGateJournalRecordKind.ChallengeCreated)
            {
                throw new InvalidOperationException(
                    "First journal record must be ChallengeCreated.");
            }

            if (expectedSequence == 2 &&
                record.Kind != WorkflowGateJournalRecordKind.AuthorizationGranted &&
                record.Kind != WorkflowGateJournalRecordKind.AuthorizationDenied)
            {
                throw new InvalidOperationException(
                    "Second journal record must be AuthorizationGranted or AuthorizationDenied.");
            }

            records.Add(record);
            expectedSequence++;
        }

        return records;
    }
}

internal enum WorkflowGateJournalRecordKind
{
    ChallengeCreated,
    AuthorizationGranted,
    AuthorizationDenied
}

internal abstract class WorkflowGateJournalRecord
{
    [JsonPropertyOrder(0)]
    public string SchemaId { get; set; } = WorkflowGateJournal.SchemaId;

    [JsonPropertyOrder(1)]
    public int SchemaVersion { get; set; } = WorkflowGateJournal.CurrentSchemaVersion;

    [JsonPropertyOrder(2)]
    public long Sequence { get; set; }

    [JsonPropertyOrder(3)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkflowGateJournalRecordKind Kind { get; set; }
}

internal sealed class ChallengeCreatedRecord : WorkflowGateJournalRecord
{
    public ChallengeCreatedRecord()
    {
        this.Kind = WorkflowGateJournalRecordKind.ChallengeCreated;
    }

    [JsonPropertyOrder(4)]
    public string WorkflowId { get; set; } = string.Empty;

    [JsonPropertyOrder(5)]
    public string TaskId { get; set; } = string.Empty;

    [JsonPropertyOrder(6)]
    public string GateId { get; set; } = string.Empty;

    [JsonPropertyOrder(7)]
    public long BasePersistenceRevision { get; set; }

    [JsonPropertyOrder(8)]
    public long GateOrdinal { get; set; } = 1;

    [JsonPropertyOrder(9)]
    public int RequiredPermission { get; set; }

    [JsonPropertyOrder(10)]
    public string BaseStateFingerprint { get; set; } = string.Empty;

    [JsonPropertyOrder(11)]
    public string InputFingerprint { get; set; } = string.Empty;

    [JsonPropertyOrder(12)]
    public string RegistryFingerprint { get; set; } = string.Empty;

    [JsonPropertyOrder(13)]
    public string PolicyFingerprint { get; set; } = string.Empty;
}

internal sealed class AuthorizationGrantedRecord : WorkflowGateJournalRecord
{
    public AuthorizationGrantedRecord()
    {
        this.Kind = WorkflowGateJournalRecordKind.AuthorizationGranted;
    }

    [JsonPropertyOrder(4)]
    public string GateId { get; set; } = string.Empty;

    [JsonPropertyOrder(5)]
    public string PrincipalId { get; set; } = string.Empty;

    [JsonPropertyOrder(6)]
    public string MechanismId { get; set; } = string.Empty;

    [JsonPropertyOrder(7)]
    public string EvidenceFingerprint { get; set; } = string.Empty;
}

internal sealed class AuthorizationDeniedRecord : WorkflowGateJournalRecord
{
    public AuthorizationDeniedRecord()
    {
        this.Kind = WorkflowGateJournalRecordKind.AuthorizationDenied;
    }

    [JsonPropertyOrder(4)]
    public string GateId { get; set; } = string.Empty;

    [JsonPropertyOrder(5)]
    public string PrincipalId { get; set; } = string.Empty;

    [JsonPropertyOrder(6)]
    public string MechanismId { get; set; } = string.Empty;

    [JsonPropertyOrder(7)]
    public string EvidenceFingerprint { get; set; } = string.Empty;
}
