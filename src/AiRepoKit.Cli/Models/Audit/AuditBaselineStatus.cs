using System.Text.Json.Serialization;

namespace AiRepoKit.Cli.Models.Audit;

[JsonConverter(typeof(JsonStringEnumConverter<AuditBaselineStatus>))]
public enum AuditBaselineStatus
{
    [JsonStringEnumMemberName("review-required")]
    ReviewRequired,

    [JsonStringEnumMemberName("accepted")]
    Accepted,

    [JsonStringEnumMemberName("false-positive")]
    FalsePositive
}
