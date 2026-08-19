using System.Text.Json.Nodes;

using Aspire.Hosting.Pipelines;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Inputs for <see cref="RailwayGraphQLApplyService"/> that are resolved by the pipeline
/// (token, adopt IDs, images) and are never written to <c>railway-plan.json</c>.
/// </summary>
public sealed class RailwayApplyRequest
{
    /// <summary>Gets or sets the account or workspace token. Never logged or written to plan files.</summary>
    public required string Token { get; init; }

    /// <summary>Gets or sets an adopted Railway project id, when <c>AsExisting</c> or state already has one.</summary>
    public string? AdoptedProjectId { get; init; }

    /// <summary>Gets or sets an adopted Railway environment id.</summary>
    public string? AdoptedEnvironmentId { get; init; }

    /// <summary>Gets or sets whether staging should duplicate production when the environment is created.</summary>
    public bool DuplicateProductionWhenCreatingStaging { get; init; } = true;

    /// <summary>Gets or sets whether a missing environment should be created empty.</summary>
    public bool CreateEmptyEnvironment { get; init; }

    /// <summary>Gets or sets resolved container image references keyed by Railway service name.</summary>
    public Dictionary<string, string> ServiceImages { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets Railway service names that should receive a public HTTP domain.</summary>
    public HashSet<string> ExternalHttpServices { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets resolved environment values keyed by Railway service name, then variable name.
    /// Deploy fills this with connection-string / parameter values; the plan itself stays
    /// secret-safe (parameter names and Railway expressions only).
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> ResolvedServiceEnvironment { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Result of a GraphQL apply. Contains Railway ids only — never tokens or bucket secrets.
/// </summary>
public sealed class RailwayApplyResult
{
    /// <summary>Gets the Railway project id that was created or adopted.</summary>
    public required string ProjectId { get; init; }

    /// <summary>Gets the Railway environment id that was created or adopted.</summary>
    public required string EnvironmentId { get; init; }

    /// <summary>Gets the production environment id when it is known (used to duplicate staging).</summary>
    public string? ProductionEnvironmentId { get; init; }

    /// <summary>Gets whether the project was created during this apply.</summary>
    public bool CreatedProject { get; init; }

    /// <summary>Gets whether the environment was created during this apply.</summary>
    public bool CreatedEnvironment { get; init; }

    /// <summary>Gets service ids keyed by Railway service name.</summary>
    public Dictionary<string, string> ServiceIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets service names as Railway returned them from <c>project(id)</c> (original casing).
    /// Used to rewrite <c>${{postgres.DATABASE_URL}}</c> to the live service name.
    /// </summary>
    public HashSet<string> AdoptedRailwayServiceNames { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Gets bucket ids keyed by Aspire resource name.</summary>
    public Dictionary<string, string> BucketIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets template codes that were applied (or already present) in this environment.</summary>
    public List<string> AppliedTemplateCodes { get; init; } = [];

    /// <summary>Gets warning messages that were reported without failing the apply.</summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Gets resolved S3 connection strings keyed by bucket resource name.
    /// In-memory only — never written to plan files or deployment state.
    /// </summary>
    public Dictionary<string, string> BucketConnectionStrings { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Tunables for GraphQL apply. Tests set poll intervals to zero so they stay offline and fast.
/// </summary>
public sealed class RailwayApplyOptions
{
    /// <summary>Gets or sets how long to wait between <c>workflowStatus</c> polls.</summary>
    public TimeSpan WorkflowPollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets or sets the maximum time to wait for a template workflow.</summary>
    public TimeSpan WorkflowTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets the time provider used for workflow deadlines. Tests may substitute a fake.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}

/// <summary>
/// Snapshot of Railway ids stored in <see cref="IDeploymentStateManager"/>.
/// </summary>
internal sealed class RailwayDeploymentSnapshot
{
    public string? ProjectId { get; set; }
    public string? EnvironmentId { get; set; }
    public string? ProductionEnvironmentId { get; set; }
    public Dictionary<string, string> EnvironmentIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ServiceIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> BucketIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> TemplateCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ProductionServiceIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ProductionBucketIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ProductionTemplateCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Reads and writes Railway deployment ids. Never stores tokens or bucket secrets.
/// </summary>
internal static class RailwayDeploymentStateStore
{
    internal const string ProjectIdKey = "ProjectId";
    internal const string EnvironmentIdKey = "EnvironmentId";
    internal const string ProductionEnvironmentIdKey = "ProductionEnvironmentId";
    internal const string EnvironmentIdsKey = "EnvironmentIds";
    internal const string ServicesKey = "Services";
    internal const string BucketsKey = "Buckets";
    internal const string TemplatesKey = "Templates";

    /// <summary>
    /// Legacy key that stored a JSON array string such as
    /// <c>["postgres"]</c>. Preview.4 never read this; load migrates it.
    /// </summary>
    internal const string AppliedTemplateCodesKey = "AppliedTemplateCodes";

    public static async Task<RailwayDeploymentSnapshot> LoadAsync(
        IDeploymentStateManager stateManager,
        string computeEnvironmentName,
        string railwayEnvironmentName,
        CancellationToken cancellationToken)
    {
        var section = await stateManager.AcquireSectionAsync($"Railway:{computeEnvironmentName}", cancellationToken)
            .ConfigureAwait(false);

        var snapshot = new RailwayDeploymentSnapshot
        {
            ProjectId = ReadString(section.Data, ProjectIdKey),
            ProductionEnvironmentId = ReadString(section.Data, ProductionEnvironmentIdKey)
        };

        CopyStringMap(section.Data[EnvironmentIdsKey] as JsonObject, snapshot.EnvironmentIds);
        if (snapshot.EnvironmentIds.TryGetValue(railwayEnvironmentName, out var scopedEnvironmentId) &&
            !string.IsNullOrWhiteSpace(scopedEnvironmentId))
        {
            snapshot.EnvironmentId = scopedEnvironmentId;
        }
        else if (string.Equals(railwayEnvironmentName, "production", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(snapshot.ProductionEnvironmentId))
        {
            snapshot.EnvironmentId = snapshot.ProductionEnvironmentId;
        }

        var servicesRoot = section.Data[ServicesKey] as JsonObject;
        CopyStringMap(servicesRoot?[railwayEnvironmentName] as JsonObject, snapshot.ServiceIds);
        CopyStringMap(servicesRoot?["production"] as JsonObject, snapshot.ProductionServiceIds);

        var bucketsRoot = section.Data[BucketsKey] as JsonObject;
        CopyStringMap(bucketsRoot?[railwayEnvironmentName] as JsonObject, snapshot.BucketIds);
        CopyStringMap(bucketsRoot?["production"] as JsonObject, snapshot.ProductionBucketIds);

        var templatesRoot = section.Data[TemplatesKey] as JsonObject;
        CopyTemplateCodes(templatesRoot?[railwayEnvironmentName], snapshot.TemplateCodes);
        CopyTemplateCodes(templatesRoot?["production"], snapshot.ProductionTemplateCodes);
        CopyTemplateCodes(section.Data[AppliedTemplateCodesKey], snapshot.TemplateCodes);

        return snapshot;
    }

    public static async Task SaveAsync(
        IDeploymentStateManager stateManager,
        string computeEnvironmentName,
        string railwayEnvironmentName,
        RailwayApplyResult result,
        CancellationToken cancellationToken)
    {
        var section = await stateManager.AcquireSectionAsync($"Railway:{computeEnvironmentName}", cancellationToken)
            .ConfigureAwait(false);

        section.Data[ProjectIdKey] = JsonValue.Create(result.ProjectId);
        section.Data[EnvironmentIdKey] = JsonValue.Create(result.EnvironmentId);
        if (!string.IsNullOrWhiteSpace(result.ProductionEnvironmentId))
        {
            section.Data[ProductionEnvironmentIdKey] = JsonValue.Create(result.ProductionEnvironmentId);
        }

        var environmentIds = section.Data[EnvironmentIdsKey] as JsonObject ?? [];
        environmentIds[railwayEnvironmentName] = result.EnvironmentId;
        section.Data[EnvironmentIdsKey] = environmentIds;

        WriteScopedMap(section.Data, ServicesKey, railwayEnvironmentName, result.ServiceIds);
        WriteScopedMap(section.Data, BucketsKey, railwayEnvironmentName, result.BucketIds);

        var templatesRoot = section.Data[TemplatesKey] as JsonObject ?? [];
        // Persist as an object (not a JSON array). Aspire FileDeploymentStateManager
        // flattens with colon keys and unflatten does not rebuild arrays — indexed
        // keys become objects with "0", so a JsonArray cast would lose the codes.
        var templates = new JsonObject();
        foreach (var code in result.AppliedTemplateCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            templates[code] = code;
        }

        templatesRoot[railwayEnvironmentName] = templates;
        section.Data[TemplatesKey] = templatesRoot;

        await stateManager.SaveSectionAsync(section, cancellationToken).ConfigureAwait(false);
    }

    private static string? ReadString(JsonObject data, string key) =>
        data[key]?.GetValue<string>();

    private static void CopyTemplateCodes(JsonNode? source, HashSet<string> destination)
    {
        if (source is null)
        {
            return;
        }

        switch (source)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    AddTemplateCode(item?.GetValue<string>(), destination);
                }

                return;
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    var fromValue = TryGetString(pair.Value);
                    if (!string.IsNullOrWhiteSpace(fromValue) && !IsFlattenedArrayIndex(fromValue))
                    {
                        destination.Add(fromValue);
                    }
                    else if (!IsFlattenedArrayIndex(pair.Key))
                    {
                        AddTemplateCode(pair.Key, destination);
                    }
                }

                return;
            default:
                CopyTemplateCodesFromString(TryGetString(source), destination);
                return;
        }
    }

    private static void CopyTemplateCodesFromString(string? raw, HashSet<string> destination)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            try
            {
                CopyTemplateCodes(JsonNode.Parse(trimmed), destination);
                return;
            }
            catch (System.Text.Json.JsonException)
            {
                // Fall through and treat the value as a comma-separated list.
            }
        }

        foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddTemplateCode(part.Trim('"'), destination);
        }
    }

    private static void AddTemplateCode(string? code, HashSet<string> destination)
    {
        if (!string.IsNullOrWhiteSpace(code) && !IsFlattenedArrayIndex(code))
        {
            destination.Add(code);
        }
    }

    private static bool IsFlattenedArrayIndex(string? value) =>
        !string.IsNullOrWhiteSpace(value) && int.TryParse(value, out _);

    private static string? TryGetString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static void CopyStringMap(JsonObject? source, Dictionary<string, string> destination)
    {
        if (source is null)
        {
            return;
        }

        foreach (var pair in source)
        {
            var value = pair.Value?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(value))
            {
                destination[pair.Key] = value;
            }
        }
    }

    private static void WriteScopedMap(
        JsonObject data,
        string rootKey,
        string railwayEnvironmentName,
        IReadOnlyDictionary<string, string> values)
    {
        var root = data[rootKey] as JsonObject ?? [];
        var scoped = root[railwayEnvironmentName] as JsonObject ?? [];
        foreach (var pair in values)
        {
            scoped[pair.Key] = pair.Value;
        }

        root[railwayEnvironmentName] = scoped;
        data[rootKey] = root;
    }
}
