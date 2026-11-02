using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pubwiz.Core.Targets.Aws;

/// <summary>
/// Makes a new revision of an ECS task definition that runs a new image: the current revision, as
/// <c>describe-task-definition</c> returns it, with one container's image replaced.
/// </summary>
public static class EcsTaskDefinition
{
    /// <summary>Fields <c>describe-task-definition</c> returns but <c>register-task-definition</c> doesn't accept.</summary>
    private static readonly string[] ReadOnlyFields =
    [
        "taskDefinitionArn", "revision", "status", "requiresAttributes", "compatibilities", "registeredAt", "registeredBy", "deregisteredAt"
    ];

    /// <summary>Returns <c>register-task-definition</c> input that runs <paramref name="image"/>.</summary>
    /// <param name="describedJson">The current revision.</param>
    /// <param name="image">The new image, e.g. <c>123.dkr.ecr.eu-west-1.amazonaws.com/shop:2a2755a</c>.</param>
    /// <param name="containerName">The container to change; when <c>null</c>, the only container, or the one already running an image of the same repository.</param>
    /// <returns>The input JSON and the name of the container that was changed.</returns>
    /// <exception cref="ArgumentException">No container, or no single container, matches.</exception>
    public static (string Json, string Container) WithImage(string describedJson, string image, string? containerName)
    {
        var definition = JsonNode.Parse(describedJson)?.AsObject() ?? throw new ArgumentException("The task definition is empty.", nameof(describedJson));
        foreach (var field in ReadOnlyFields)
        {
            definition.Remove(field);
        }

        var containers = definition["containerDefinitions"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
        var container = FindContainer(containers, image, containerName);
        container["image"] = image;

        var json = definition.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        return (json, container["name"]!.GetValue<string>());
    }

    private static JsonObject FindContainer(List<JsonObject> containers, string image, string? containerName)
    {
        var names = string.Join(", ", containers.Select(c => c["name"]?.GetValue<string>()));
        if (containerName is not null)
        {
            return containers.FirstOrDefault(c => c["name"]?.GetValue<string>() == containerName)
                   ?? throw new ArgumentException($"The task definition has no container named '{containerName}' (it has {names}).");
        }

        if (containers.Count == 1)
        {
            return containers[0];
        }

        var repository = RepositoryOf(image);
        var matches = containers.Where(c => c["image"]?.GetValue<string>() is { } current && RepositoryOf(current) == repository).ToList();
        return matches.Count == 1
            ? matches[0]
            : throw new ArgumentException($"The task definition has several containers ({names}); set {Profiles.ProfileProperties.PubwizAwsContainerName} to the one that runs this app.");
    }

    /// <summary>The image without its tag or digest, e.g. <c>registry/shop</c> for <c>registry/shop:1.0</c>.</summary>
    internal static string RepositoryOf(string image)
    {
        var withoutDigest = image.Split('@')[0];
        var lastSlash = withoutDigest.LastIndexOf('/');
        var tagColon = withoutDigest.IndexOf(':', lastSlash + 1);
        return tagColon < 0 ? withoutDigest : withoutDigest[..tagColon];
    }
}
