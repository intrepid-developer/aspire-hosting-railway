using System.Text.Json.Nodes;

using Aspire.Hosting.Pipelines;

namespace Aspire.Hosting.Railway.Tests;

public class RailwayDeploymentStateStoreTests
{
    [Fact]
    public async Task Save_ThenFlattenUnflatten_KeepsAppliedTemplateCodes()
    {
        var state = new MemoryDeploymentStateManager();
        await RailwayDeploymentStateStore.SaveAsync(
            state,
            "railway",
            "production",
            CreateResult(templateCodes: ["postgres"]),
            CancellationToken.None);

        await FlattenUnflattenSectionAsync(state, "Railway:railway");

        var snapshot = await RailwayDeploymentStateStore.LoadAsync(
            state,
            "railway",
            "production",
            CancellationToken.None);

        Assert.Contains("postgres", snapshot.TemplateCodes);
        var section = await state.AcquireSectionAsync("Railway:railway");
        Assert.IsType<JsonObject>(section.Data[RailwayDeploymentStateStore.TemplatesKey]?["production"]);
        Assert.IsNotType<JsonArray>(section.Data[RailwayDeploymentStateStore.TemplatesKey]?["production"]);
    }

    [Fact]
    public async Task Load_FlattenedArrayIndexes_StillContainsPostgres()
    {
        var state = new MemoryDeploymentStateManager();
        var section = await state.AcquireSectionAsync("Railway:railway");
        section.Data[RailwayDeploymentStateStore.ProjectIdKey] = GraphQLFixtures.ProjectId;
        section.Data[RailwayDeploymentStateStore.TemplatesKey] = new JsonObject
        {
            ["production"] = new JsonArray("postgres")
        };
        await state.SaveSectionAsync(section);

        await FlattenUnflattenSectionAsync(state, "Railway:railway");

        var flattened = await state.AcquireSectionAsync("Railway:railway");
        var production = Assert.IsType<JsonObject>(
            flattened.Data[RailwayDeploymentStateStore.TemplatesKey]?["production"]);
        Assert.True(production.ContainsKey("0"));
        Assert.IsNotType<JsonArray>(flattened.Data[RailwayDeploymentStateStore.TemplatesKey]?["production"]);

        var snapshot = await RailwayDeploymentStateStore.LoadAsync(
            state,
            "railway",
            "production",
            CancellationToken.None);

        Assert.Contains("postgres", snapshot.TemplateCodes);
    }

    [Fact]
    public async Task Load_MigratesLegacyAppliedTemplateCodesString()
    {
        var state = new MemoryDeploymentStateManager();
        var section = await state.AcquireSectionAsync("Railway:railway");
        section.Data[RailwayDeploymentStateStore.ProjectIdKey] = GraphQLFixtures.ProjectId;
        section.Data[RailwayDeploymentStateStore.AppliedTemplateCodesKey] = """["postgres"]""";
        await state.SaveSectionAsync(section);

        var snapshot = await RailwayDeploymentStateStore.LoadAsync(
            state,
            "railway",
            "production",
            CancellationToken.None);

        Assert.Contains("postgres", snapshot.TemplateCodes);
    }

    private static RailwayApplyResult CreateResult(params string[] templateCodes)
    {
        var result = new RailwayApplyResult
        {
            ProjectId = GraphQLFixtures.ProjectId,
            EnvironmentId = GraphQLFixtures.ProductionEnvironmentId,
            ProductionEnvironmentId = GraphQLFixtures.ProductionEnvironmentId
        };
        result.AppliedTemplateCodes.AddRange(templateCodes);
        result.ServiceIds["api"] = GraphQLFixtures.ApiServiceId;
        result.BucketIds["uploads"] = GraphQLFixtures.BucketId;
        return result;
    }

    /// <summary>
    /// Simulates Aspire <c>FileDeploymentStateManager</c> / <c>JsonFlattener</c>:
    /// flatten with colon keys, then unflatten without rebuilding arrays
    /// (indexed keys become objects with <c>"0"</c>, not <c>JsonArray</c>).
    /// </summary>
    internal static async Task FlattenUnflattenSectionAsync(MemoryDeploymentStateManager state, string sectionName)
    {
        var section = await state.AcquireSectionAsync(sectionName);
        var flattened = FlattenJsonObject(section.Data);
        var unflattened = UnflattenJsonObject(flattened);
        var rewritten = new DeploymentStateSection(sectionName, unflattened, section.Version);
        await state.SaveSectionAsync(rewritten);
    }

    internal static JsonObject FlattenJsonObject(JsonObject source)
    {
        var result = new JsonObject();
        FlattenJsonObjectRecursive(source, string.Empty, result);
        return result;
    }

    internal static JsonObject UnflattenJsonObject(JsonObject source)
    {
        var result = new JsonObject();
        foreach (var pair in source)
        {
            var keys = pair.Key.Split(':');
            var current = result;
            for (var i = 0; i < keys.Length - 1; i++)
            {
                if (!current.TryGetPropertyValue(keys[i], out var existing) || existing is not JsonObject)
                {
                    var next = new JsonObject();
                    current[keys[i]] = next;
                    current = next;
                }
                else
                {
                    current = existing.AsObject();
                }
            }

            current[keys[^1]] = pair.Value?.DeepClone();
        }

        return result;
    }

    private static void FlattenJsonObjectRecursive(JsonObject source, string prefix, JsonObject result)
    {
        foreach (var pair in source)
        {
            var key = string.IsNullOrEmpty(prefix)
                ? pair.Key
                : string.IsNullOrEmpty(pair.Key)
                    ? prefix
                    : $"{prefix}:{pair.Key}";

            if (pair.Value is JsonObject nested)
            {
                FlattenJsonObjectRecursive(nested, key, result);
            }
            else if (pair.Value is JsonArray array)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    var arrayKey = $"{key}:{i}";
                    if (array[i] is JsonObject arrayObject)
                    {
                        FlattenJsonObjectRecursive(arrayObject, arrayKey, result);
                    }
                    else
                    {
                        result[arrayKey] = array[i]?.DeepClone();
                    }
                }
            }
            else
            {
                result[key] = pair.Value?.DeepClone();
            }
        }
    }
}
