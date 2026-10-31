using System.Text.Json.Serialization;

namespace Pubwiz.Core.Targets.Azure.Kudu;

/// <summary>
/// Kudu deployment states, as returned by <c>/api/deployments/{id}</c>.
/// </summary>
public enum KuduDeploymentStatus
{
    Unknown = -10,
    Cancelled = -1,
    Pending = 0,
    Building = 1,
    Deploying = 2,
    Failed = 3,
    Success = 4,
    Conflict = 5,
    PartialSuccess = 6
}

/// <summary>
/// The status document of a Kudu deployment.
/// </summary>
public sealed class KuduDeployment
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("status")]
    public KuduDeploymentStatus Status { get; init; } = KuduDeploymentStatus.Unknown;

    [JsonPropertyName("status_text")]
    public string? StatusText { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("log_url")]
    public string? LogUrl { get; init; }

    public bool IsSuccessful => Status is KuduDeploymentStatus.Success or KuduDeploymentStatus.PartialSuccess;

    public bool IsFinished => IsSuccessful || Status is KuduDeploymentStatus.Failed or KuduDeploymentStatus.Conflict or KuduDeploymentStatus.Cancelled;
}
