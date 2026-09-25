namespace AiRepoKit.Execution;

using System.Text;
using System.Text.Json;
using AiRepoKit.Agents;

public static class PromptCompiler
{
    public const string AlgorithmId =
        "ai.repokit.prompt-compiler/v1";

    public static CompiledPrompt Compile(
        ExecutableWork work_,
        string taskId_,
        CompiledContext context_)
    {
        ArgumentNullException.ThrowIfNull(
            work_,
            nameof(work_));

        ArgumentException.ThrowIfNullOrWhiteSpace(
            taskId_,
            nameof(taskId_));

        ArgumentNullException.ThrowIfNull(
            context_,
            nameof(context_));

        ExecutableTask? task =
            null;

        for (int index = 0; index < work_.Tasks.Count; index++)
        {
            ExecutableTask candidate =
                work_.Tasks[index];

            if (string.Equals(
                    candidate.Id,
                    taskId_,
                    StringComparison.Ordinal))
            {
                task =
                    candidate;

                break;
            }
        }

        if (task is null)
        {
            throw new ArgumentException(
                $"Unknown task identifier '{taskId_}'.",
                nameof(taskId_));
        }

        ValidationRequirement[] validationRequirements =
            work_
                .ValidationRequirements
                .Where(
                    requirement_ =>
                        string.Equals(
                            requirement_.TaskId,
                            task.Id,
                            StringComparison.Ordinal))
                .OrderBy(
                    requirement_ =>
                        requirement_.Id,
                    StringComparer.Ordinal)
                .ToArray();

        ModelRequirement? modelRequirement =
            null;

        for (
            int index = 0;
            index < work_.ModelRequirements.Count;
            index++
        )
        {
            ModelRequirement candidate =
                work_.ModelRequirements[index];

            if (string.Equals(
                    candidate.TaskId,
                    task.Id,
                    StringComparison.Ordinal))
            {
                modelRequirement =
                    candidate;

                break;
            }
        }

        AgentRequirement? agentRequirement =
            null;

        for (
            int index = 0;
            index < work_.AgentRequirements.Count;
            index++
        )
        {
            AgentRequirement candidate =
                work_.AgentRequirements[index];

            if (string.Equals(
                    candidate.TaskId,
                    task.Id,
                    StringComparison.Ordinal))
            {
                agentRequirement =
                    candidate;

                break;
            }
        }

        using MemoryStream stream =
            new();

        using (
            Utf8JsonWriter writer =
                new(
                    stream,
                    new JsonWriterOptions
                    {
                        Indented = false
                    }))
        {
            writer.WriteStartObject();

            writer.WriteString(
                "algorithmId",
                AlgorithmId);

            writer.WritePropertyName(
                "task");

            writer.WriteStartObject();

            writer.WriteString(
                "id",
                task.Id);

            writer.WriteString(
                "sourcePlanStepId",
                task.SourcePlanStepId);

            writer.WriteString(
                "instruction",
                task.Instruction);

            writer.WriteEndObject();

            writer.WritePropertyName(
                "context");

            writer.WriteStartObject();

            writer.WriteBoolean(
                "truncated",
                context_.Truncated);

            writer.WritePropertyName(
                "items");

            writer.WriteStartArray();

            for (
                int index = 0;
                index < context_.Items.Count;
                index++
            )
            {
                CompiledContextItem item =
                    context_.Items[index];

                writer.WriteStartObject();

                writer.WriteString(
                    "id",
                    item.Id);

                writer.WriteString(
                    "content",
                    item.Content);

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WritePropertyName(
                "validationRequirements");

            writer.WriteStartArray();

            for (
                int index = 0;
                index < validationRequirements.Length;
                index++
            )
            {
                ValidationRequirement requirement =
                    validationRequirements[index];

                writer.WriteStartObject();

                writer.WriteString(
                    "id",
                    requirement.Id);

                writer.WriteString(
                    "sourceAcceptanceCriterionId",
                    requirement.SourceAcceptanceCriterionId);

                writer.WriteString(
                    "strategy",
                    GetValidationStrategyText(
                        requirement.Strategy));

                writer.WriteString(
                    "statement",
                    requirement.Statement);

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            WriteCapabilities(
                writer,
                "modelRequiredCapabilities",
                modelRequirement?.RequiredCapabilities);

            WriteCapabilities(
                writer,
                "agentRequiredCapabilities",
                agentRequirement?.RequiredCapabilities);

            writer.WriteEndObject();
            writer.Flush();
        }

        string content =
            Encoding.UTF8.GetString(
                stream.ToArray());

        return new CompiledPrompt(
            task.Id,
            content);
    }

    private static void WriteCapabilities(
        Utf8JsonWriter writer_,
        string propertyName_,
        AgentCapabilitySet? capabilities_)
    {
        writer_.WritePropertyName(
            propertyName_);

        writer_.WriteStartArray();

        if (capabilities_ is not null)
        {
            foreach (AgentCapability capability in capabilities_)
            {
                writer_.WriteStringValue(
                    capability.Value);
            }
        }

        writer_.WriteEndArray();
    }

    private static string GetValidationStrategyText(
        ValidationStrategy strategy_)
    {
        return strategy_ switch
        {
            ValidationStrategy.Build => "Build",
            ValidationStrategy.Test => "Test",
            ValidationStrategy.Policy => "Policy",
            _ => throw new ArgumentOutOfRangeException(
                nameof(strategy_),
                strategy_,
                "Unsupported validation strategy.")
        };
    }
}
