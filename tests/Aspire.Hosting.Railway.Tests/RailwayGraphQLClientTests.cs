using System.Net;
using System.Text;

using Aspire.Hosting.Railway;

namespace Aspire.Hosting.Railway.Tests;

public class RailwayGraphQLClientTests
{
    [Fact]
    public async Task ProjectCreate_PostsExpectedOperationShape()
    {
        var handler = new RecordingHandler("""{"data":{"projectCreate":{"id":"proj_placeholder","name":"demo"}}}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        var response = await client.ProjectCreateAsync(
            new ProjectCreateInput { Name = "demo" },
            "placeholder-token");

        Assert.Equal("proj_placeholder", response.Data?.ProjectCreate?.Id);
        Assert.Contains("projectCreate", handler.Body, StringComparison.Ordinal);
        Assert.Contains("ProjectCreateInput", handler.Body, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(RailwayGraphQLClient.DefaultEndpoint, handler.Uri?.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("placeholder-token", handler.AuthorizationParameter);
        Assert.DoesNotContain("placeholder-token", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceCreate_AlwaysIncludesEnvironmentId()
    {
        var handler = new RecordingHandler("""{"data":{"serviceCreate":{"id":"svc_placeholder","name":"api"}}}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.ServiceCreateAsync(
            new ServiceCreateInput
            {
                ProjectId = "proj_placeholder",
                EnvironmentId = "env_placeholder",
                Name = "api"
            },
            "placeholder-token");

        Assert.Contains("serviceCreate", handler.Body, StringComparison.Ordinal);
        Assert.Contains("environmentId", handler.Body, StringComparison.Ordinal);
        Assert.Contains("env_placeholder", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TemplateAndBucketOperations_UseConfirmedFieldNames()
    {
        var handler = new RecordingHandler("""{"data":{}}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.TemplateAsync("postgres", "placeholder-token");
        Assert.Contains("template", handler.Body, StringComparison.Ordinal);
        Assert.Contains("serializedConfig", handler.Body, StringComparison.Ordinal);

        await client.BucketCreateAsync(
            new BucketCreateInput
            {
                ProjectId = "proj_placeholder",
                EnvironmentId = "env_placeholder",
                Name = "uploads",
                Region = "us-west2"
            },
            "placeholder-token");
        Assert.Contains("bucketCreate", handler.Body, StringComparison.Ordinal);

        await client.EnvironmentCreateAsync(
            new EnvironmentCreateInput
            {
                ProjectId = "proj_placeholder",
                Name = "staging",
                SourceEnvironmentId = "env_production_placeholder"
            },
            "placeholder-token");
        Assert.Contains("environmentCreate", handler.Body, StringComparison.Ordinal);
        Assert.Contains("sourceEnvironmentId", handler.Body, StringComparison.Ordinal);

        await client.WorkflowStatusAsync("wf_placeholder", "placeholder-token");
        Assert.Contains("workflowStatus", handler.Body, StringComparison.Ordinal);
        Assert.Contains("error", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BucketS3Credentials_RequestIncludesProjectIdAndSelectsBucketName()
    {
        var handler = new RecordingHandler(
            """{"data":{"bucketS3Credentials":{"accessKeyId":"placeholder-access-key","secretAccessKey":"placeholder-secret-key","endpoint":"https://storage.railway.app","region":"auto","bucketName":"uploads"}}}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        var response = await client.BucketS3CredentialsAsync(
            "bucket_placeholder",
            "env_placeholder",
            "proj_placeholder",
            "placeholder-token");

        Assert.Equal("uploads", response.Data?.BucketS3Credentials?.BucketName);
        Assert.Contains("projectId", handler.Body, StringComparison.Ordinal);
        Assert.Contains("proj_placeholder", handler.Body, StringComparison.Ordinal);
        Assert.Contains("bucketName", handler.Body, StringComparison.Ordinal);
        Assert.Contains("$projectId: String!", RailwayGraphQLOperations.BucketS3Credentials, StringComparison.Ordinal);
        Assert.Contains("bucketName", RailwayGraphQLOperations.BucketS3Credentials, StringComparison.Ordinal);
        Assert.DoesNotContain("region\n            bucket\n", RailwayGraphQLOperations.BucketS3Credentials, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BucketS3Credentials_LiveArrayPayload_TakesFirstCredential()
    {
        var handler = new RecordingHandler(
            """{"data":{"bucketS3Credentials":[{"accessKeyId":"placeholder-access-key","secretAccessKey":"placeholder-secret-key","endpoint":"https://storage.railway.app","region":"auto","bucketName":"uploads"}]}}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        var response = await client.BucketS3CredentialsAsync(
            "bucket_placeholder",
            "env_placeholder",
            "proj_placeholder",
            "placeholder-token");

        RailwayGraphQLClient.ThrowIfFailed(response, "bucketS3Credentials");
        Assert.Equal("uploads", response.Data?.BucketS3Credentials?.BucketName);
        Assert.Equal("placeholder-access-key", response.Data?.BucketS3Credentials?.AccessKeyId);
        Assert.Equal("https://storage.railway.app", response.Data?.BucketS3Credentials?.Endpoint);
    }

    [Fact]
    public async Task Project_PostsDocumentedQueryWithServiceIds()
    {
        var handler = new RecordingHandler(GraphQLFixtures.ProjectWithExistingCanvas);
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        var response = await client.ProjectAsync(GraphQLFixtures.ProjectId, "placeholder-token");

        Assert.Equal("Postgres", response.Data?.Project?.Services?.Edges?[0].Node?.Name);
        Assert.Contains("\"operationName\":\"project\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("services", handler.Body, StringComparison.Ordinal);
        Assert.Contains("environments", handler.Body, StringComparison.Ordinal);
        Assert.Contains("buckets", handler.Body, StringComparison.Ordinal);
        Assert.Contains("id", RailwayGraphQLOperations.Project, StringComparison.Ordinal);
        Assert.Contains("buckets", RailwayGraphQLOperations.Project, StringComparison.Ordinal);
        Assert.DoesNotContain("pluginCreate", RailwayGraphQLOperations.Project, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Project_DeserializesExistingBucketsByName()
    {
        var handler = new RecordingHandler(GraphQLFixtures.ProjectWithExistingBucket);
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        var response = await client.ProjectAsync(GraphQLFixtures.ProjectId, "placeholder-token");

        var bucket = Assert.Single(response.Data?.Project?.Buckets?.Edges ?? []);
        Assert.Equal(GraphQLFixtures.BucketId, bucket.Node?.Id);
        Assert.Equal("Uploads", bucket.Node?.Name);
        Assert.Equal(GraphQLFixtures.UploadsServiceId, response.Data?.Project?.Services?.Edges?[1].Node?.Id);
        Assert.NotEqual(bucket.Node?.Id, response.Data?.Project?.Services?.Edges?[1].Node?.Id);
    }

    [Fact]
    public async Task ApplyTemplateAsync_FetchesSerializedConfigThenDeploys()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("template", GraphQLFixtures.TemplatePostgres);
        handler.Enqueue("templateDeployV2", GraphQLFixtures.TemplateDeployV2);
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        var response = await client.ApplyTemplateAsync(
            "postgres",
            GraphQLFixtures.ProjectId,
            GraphQLFixtures.ProductionEnvironmentId,
            "placeholder-token");

        Assert.Equal("wf_placeholder", response.Data?.TemplateDeployV2?.WorkflowId);
        Assert.Equal(new[] { "template", "templateDeployV2" }, handler.Operations);
        Assert.Contains("serializedConfig", handler.Bodies[1], StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", handler.Bodies[1], StringComparison.Ordinal);

        var fetchedId = GraphQLFixtures.ReadTemplateIdFromResponse(GraphQLFixtures.TemplatePostgres);
        Assert.Equal("tpl_postgres_placeholder", fetchedId);
        Assert.Equal(fetchedId, GraphQLFixtures.ReadTemplateIdFromDeployBody(handler.Bodies[1]));
    }

    [Fact]
    public async Task ApplyTemplateAsync_MissingTemplateId_DoesNotCallTemplateDeployV2()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("template", """{"data":{"template":{"code":"postgres","serializedConfig":{"services":{"postgres":{}}}}}}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ApplyTemplateAsync(
            "postgres",
            GraphQLFixtures.ProjectId,
            GraphQLFixtures.ProductionEnvironmentId,
            "placeholder-token"));

        Assert.Contains("did not return id", exception.Message, StringComparison.Ordinal);
        Assert.Contains("templateId", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Count("templateDeployV2"));
    }

    [Fact]
    public async Task ServiceInstanceUpdate_ImageOnly_OmitsScaleFields()
    {
        var handler = new RecordingHandler("""{"data":true}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.ServiceInstanceUpdateAsync(
            "svc_placeholder",
            "env_placeholder",
            new ServiceInstanceUpdateInput
            {
                Source = new ServiceSourceInput { Image = "nginx" }
            },
            "placeholder-token");

        using var document = System.Text.Json.JsonDocument.Parse(handler.Body);
        var input = document.RootElement.GetProperty("variables").GetProperty("input");
        Assert.Equal("nginx", input.GetProperty("source").GetProperty("image").GetString());
        Assert.False(input.TryGetProperty("multiRegionConfig", out _));
        Assert.False(input.TryGetProperty("sleepApplication", out _));
        Assert.False(input.TryGetProperty("numReplicas", out _));
        Assert.False(input.TryGetProperty("region", out _));
        Assert.False(input.TryGetProperty("healthcheckPath", out _));
        Assert.False(input.TryGetProperty("healthcheckTimeout", out _));
        Assert.False(input.TryGetProperty("restartPolicyType", out _));
        Assert.False(input.TryGetProperty("restartPolicyMaxRetries", out _));
        Assert.Contains("serviceInstanceUpdate", handler.Body, StringComparison.Ordinal);
        Assert.Contains("environmentId", handler.Body, StringComparison.Ordinal);
        Assert.Contains("env_placeholder", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceInstanceUpdate_SerializesMultiRegionConfigAndSleepApplication()
    {
        var handler = new RecordingHandler("""{"data":true}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.ServiceInstanceUpdateAsync(
            "svc_placeholder",
            "env_placeholder",
            new ServiceInstanceUpdateInput
            {
                Source = new ServiceSourceInput { Image = "nginx" },
                MultiRegionConfig = new Dictionary<string, ServiceInstanceRegionConfig>(StringComparer.Ordinal)
                {
                    ["us-west2"] = new() { NumReplicas = 2 },
                    ["europe-west4-drams3a"] = new() { NumReplicas = 1 }
                },
                SleepApplication = true
            },
            "placeholder-token");

        using var document = System.Text.Json.JsonDocument.Parse(handler.Body);
        var input = document.RootElement.GetProperty("variables").GetProperty("input");
        var multiRegion = input.GetProperty("multiRegionConfig");
        Assert.Equal("nginx", input.GetProperty("source").GetProperty("image").GetString());
        Assert.Equal(2, multiRegion.GetProperty("us-west2").GetProperty("numReplicas").GetInt32());
        Assert.Equal(1, multiRegion.GetProperty("europe-west4-drams3a").GetProperty("numReplicas").GetInt32());
        Assert.True(input.GetProperty("sleepApplication").GetBoolean());
        Assert.False(input.TryGetProperty("numReplicas", out _));
        Assert.False(input.TryGetProperty("region", out _));
        Assert.DoesNotContain("replicaRegions", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("serverless", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceInstanceUpdate_SerializesHealthcheckPathAndTimeout()
    {
        var handler = new RecordingHandler("""{"data":true}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.ServiceInstanceUpdateAsync(
            "svc_placeholder",
            "env_placeholder",
            new ServiceInstanceUpdateInput
            {
                Source = new ServiceSourceInput { Image = "nginx" },
                HealthcheckPath = "/health",
                HealthcheckTimeout = 120
            },
            "placeholder-token");

        using var document = System.Text.Json.JsonDocument.Parse(handler.Body);
        var input = document.RootElement.GetProperty("variables").GetProperty("input");
        Assert.Equal("/health", input.GetProperty("healthcheckPath").GetString());
        Assert.Equal(120, input.GetProperty("healthcheckTimeout").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Number, input.GetProperty("healthcheckTimeout").ValueKind);
        Assert.False(input.TryGetProperty("RAILWAY_HEALTHCHECK_TIMEOUT_SEC", out _));
        Assert.DoesNotContain("null", handler.Body, StringComparison.Ordinal);
        Assert.Contains("environmentId", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceInstanceUpdate_SerializesRestartPolicyTypeAndMaxRetries()
    {
        var handler = new RecordingHandler("""{"data":true}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.ServiceInstanceUpdateAsync(
            "svc_placeholder",
            "env_placeholder",
            new ServiceInstanceUpdateInput
            {
                Source = new ServiceSourceInput { Image = "nginx" },
                RestartPolicyType = "ON_FAILURE",
                RestartPolicyMaxRetries = 10
            },
            "placeholder-token");

        using var document = System.Text.Json.JsonDocument.Parse(handler.Body);
        var input = document.RootElement.GetProperty("variables").GetProperty("input");
        Assert.Equal("ON_FAILURE", input.GetProperty("restartPolicyType").GetString());
        Assert.Equal(10, input.GetProperty("restartPolicyMaxRetries").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Number, input.GetProperty("restartPolicyMaxRetries").ValueKind);
        Assert.False(input.TryGetProperty("healthcheckPath", out _));
        Assert.DoesNotContain("null", handler.Body, StringComparison.Ordinal);
        Assert.Contains("environmentId", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceInstanceUpdate_SerializesStartCommandAndPreDeployCommand()
    {
        var handler = new RecordingHandler("""{"data":true}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.ServiceInstanceUpdateAsync(
            "svc_placeholder",
            "env_placeholder",
            new ServiceInstanceUpdateInput
            {
                Source = new ServiceSourceInput { Image = "nginx" },
                StartCommand = "/bin/sh -c \"exec ./api\"",
                PreDeployCommand = ["dotnet MyApp.dll --migrate"]
            },
            "placeholder-token");

        using var document = System.Text.Json.JsonDocument.Parse(handler.Body);
        var input = document.RootElement.GetProperty("variables").GetProperty("input");
        Assert.Equal("/bin/sh -c \"exec ./api\"", input.GetProperty("startCommand").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Array, input.GetProperty("preDeployCommand").ValueKind);
        Assert.Equal(1, input.GetProperty("preDeployCommand").GetArrayLength());
        Assert.Equal("dotnet MyApp.dll --migrate", input.GetProperty("preDeployCommand")[0].GetString());
        Assert.False(input.TryGetProperty("healthcheckPath", out _));
        Assert.DoesNotContain("null", handler.Body, StringComparison.Ordinal);
        Assert.Contains("environmentId", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceInstanceUpdate_SerializesOverlapAndDrainingSeconds()
    {
        var handler = new RecordingHandler("""{"data":true}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.ServiceInstanceUpdateAsync(
            "svc_placeholder",
            "env_placeholder",
            new ServiceInstanceUpdateInput
            {
                Source = new ServiceSourceInput { Image = "nginx" },
                OverlapSeconds = 60,
                DrainingSeconds = 10
            },
            "placeholder-token");

        using var document = System.Text.Json.JsonDocument.Parse(handler.Body);
        var input = document.RootElement.GetProperty("variables").GetProperty("input");
        Assert.Equal(System.Text.Json.JsonValueKind.Number, input.GetProperty("overlapSeconds").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Number, input.GetProperty("drainingSeconds").ValueKind);
        Assert.Equal(60, input.GetProperty("overlapSeconds").GetInt32());
        Assert.Equal(10, input.GetProperty("drainingSeconds").GetInt32());
        Assert.False(input.TryGetProperty("startCommand", out _));
        Assert.False(input.TryGetProperty("RAILWAY_DEPLOYMENT_OVERLAP_SECONDS", out _));
        Assert.False(input.TryGetProperty("RAILWAY_DEPLOYMENT_DRAINING_SECONDS", out _));
        Assert.DoesNotContain("null", handler.Body, StringComparison.Ordinal);
        Assert.Contains("environmentId", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceInstanceUpdate_SerializesCronSchedule()
    {
        var handler = new RecordingHandler("""{"data":true}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.ServiceInstanceUpdateAsync(
            "svc_placeholder",
            "env_placeholder",
            new ServiceInstanceUpdateInput
            {
                Source = new ServiceSourceInput { Image = "nginx" },
                CronSchedule = "0 3 * * *"
            },
            "placeholder-token");

        using var document = System.Text.Json.JsonDocument.Parse(handler.Body);
        var input = document.RootElement.GetProperty("variables").GetProperty("input");
        Assert.Equal("0 3 * * *", input.GetProperty("cronSchedule").GetString());
        Assert.False(input.TryGetProperty("startCommand", out _));
        Assert.DoesNotContain("null", handler.Body, StringComparison.Ordinal);
        Assert.Contains("environmentId", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("cronCreate", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("scheduleCreate", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomDomainCreate_SerializesConfirmedFieldNamesAndOmitsUnset()
    {
        var handler = new RecordingHandler(GraphQLFixtures.CustomDomainCreate);
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        var response = await client.CustomDomainCreateAsync(
            new CustomDomainCreateInput
            {
                Domain = "api.example.com",
                EnvironmentId = "env_placeholder",
                ProjectId = "proj_placeholder",
                ServiceId = "svc_placeholder",
                TargetPort = 8080
            },
            "placeholder-token");

        Assert.Equal("cdom_placeholder", response.Data?.CustomDomainCreate?.Id);
        Assert.Equal("verify-placeholder", response.Data?.CustomDomainCreate?.Status?.VerificationToken);
        using var document = System.Text.Json.JsonDocument.Parse(handler.Body);
        var input = document.RootElement.GetProperty("variables").GetProperty("input");
        Assert.Equal("api.example.com", input.GetProperty("domain").GetString());
        Assert.Equal("env_placeholder", input.GetProperty("environmentId").GetString());
        Assert.Equal("proj_placeholder", input.GetProperty("projectId").GetString());
        Assert.Equal("svc_placeholder", input.GetProperty("serviceId").GetString());
        Assert.Equal(8080, input.GetProperty("targetPort").GetInt32());
        Assert.False(input.TryGetProperty("verificationToken", out _));
        Assert.Contains("customDomainCreate", handler.Body, StringComparison.Ordinal);
        Assert.Contains("CustomDomainCreateInput", handler.Body, StringComparison.Ordinal);
        Assert.Contains("$input: CustomDomainCreateInput!", RailwayGraphQLOperations.CustomDomainCreate, StringComparison.Ordinal);
        Assert.DoesNotContain("null", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("customDomainDelete", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("pluginCreate", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("dnsRecords.verificationToken", RailwayGraphQLOperations.CustomDomainCreate, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomDomainCreate_OmitsTargetPortWhenUnset()
    {
        var handler = new RecordingHandler(GraphQLFixtures.CustomDomainCreate);
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.CustomDomainCreateAsync(
            new CustomDomainCreateInput
            {
                Domain = "api.example.com",
                EnvironmentId = "env_placeholder",
                ProjectId = "proj_placeholder",
                ServiceId = "svc_placeholder"
            },
            "placeholder-token");

        using var document = System.Text.Json.JsonDocument.Parse(handler.Body);
        var input = document.RootElement.GetProperty("variables").GetProperty("input");
        Assert.False(input.TryGetProperty("targetPort", out _));
        Assert.DoesNotContain("null", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceDomainCreate_OmitsTargetPortWhenUnset()
    {
        var handler = new RecordingHandler(GraphQLFixtures.ServiceDomainCreate);
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.ServiceDomainCreateAsync(
            new ServiceDomainCreateInput
            {
                ServiceId = "svc_placeholder",
                EnvironmentId = "env_placeholder"
            },
            "placeholder-token");

        using var document = System.Text.Json.JsonDocument.Parse(handler.Body);
        var input = document.RootElement.GetProperty("variables").GetProperty("input");
        Assert.Equal("svc_placeholder", input.GetProperty("serviceId").GetString());
        Assert.Equal("env_placeholder", input.GetProperty("environmentId").GetString());
        Assert.False(input.TryGetProperty("targetPort", out _));
        Assert.DoesNotContain("null", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DomainsAndAvailability_PostConfirmedShapes()
    {
        var handler = new RecordingHandler(GraphQLFixtures.DomainsEmpty);
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.DomainsAsync("env_placeholder", "proj_placeholder", "svc_placeholder", "placeholder-token");
        Assert.Contains("\"operationName\":\"domains\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("environmentId", handler.Body, StringComparison.Ordinal);
        Assert.Contains("projectId", handler.Body, StringComparison.Ordinal);
        Assert.Contains("serviceId", handler.Body, StringComparison.Ordinal);
        Assert.Contains("customDomains", RailwayGraphQLOperations.Domains, StringComparison.Ordinal);
        Assert.Contains("serviceDomains", RailwayGraphQLOperations.Domains, StringComparison.Ordinal);
        Assert.Contains("verificationToken", RailwayGraphQLOperations.Domains, StringComparison.Ordinal);
        Assert.DoesNotContain("customDomainDelete", RailwayGraphQLOperations.Domains, StringComparison.Ordinal);

        await client.CustomDomainAvailableAsync("api.example.com", "placeholder-token");
        Assert.Contains("\"operationName\":\"customDomainAvailable\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"domain\":\"api.example.com\"", handler.Body, StringComparison.Ordinal);

        await client.CustomDomainAsync("cdom_placeholder", "proj_placeholder", "placeholder-token");
        Assert.Contains("\"operationName\":\"customDomain\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"cdom_placeholder\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"projectId\":\"proj_placeholder\"", handler.Body, StringComparison.Ordinal);

        await client.CustomDomainUpdateAsync("env_placeholder", "cdom_placeholder", 80, "placeholder-token");
        Assert.Contains("\"operationName\":\"customDomainUpdate\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"environmentId\":\"env_placeholder\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"targetPort\":80", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("null", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("customDomainDelete", RailwayGraphQLOperations.CustomDomainUpdate, StringComparison.Ordinal);
        Assert.DoesNotContain("customDomainIssueCertificate", RailwayGraphQLOperations.CustomDomainCreate, StringComparison.Ordinal);
        Assert.DoesNotContain("pluginCreate", RailwayGraphQLOperations.CustomDomainCreate, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceInstanceLimitsUpdate_SerializesConfirmedFieldNames()
    {
        var handler = new RecordingHandler("""{"data":true}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.ServiceInstanceLimitsUpdateAsync(
            new ServiceInstanceLimitsUpdateInput
            {
                ServiceId = "svc_placeholder",
                EnvironmentId = "env_placeholder",
                VCpus = 1,
                MemoryGb = 2
            },
            "placeholder-token");

        using var document = System.Text.Json.JsonDocument.Parse(handler.Body);
        var input = document.RootElement.GetProperty("variables").GetProperty("input");
        Assert.Equal("svc_placeholder", input.GetProperty("serviceId").GetString());
        Assert.Equal("env_placeholder", input.GetProperty("environmentId").GetString());
        Assert.Equal(1, input.GetProperty("vCPUs").GetDouble());
        Assert.Equal(2, input.GetProperty("memoryGB").GetDouble());
        Assert.Contains("serviceInstanceLimitsUpdate", handler.Body, StringComparison.Ordinal);
        Assert.Contains("ServiceInstanceLimitsUpdateInput", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"vCPUs\":", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"memoryGB\":", handler.Body, StringComparison.Ordinal);
        Assert.Contains("$input: ServiceInstanceLimitsUpdateInput!", RailwayGraphQLOperations.ServiceInstanceLimitsUpdate, StringComparison.Ordinal);
        Assert.DoesNotContain("vCpus", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"memoryGb\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("memoryBytes", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("limitOverride", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", handler.Body, StringComparison.Ordinal);
        Assert.False(input.TryGetProperty("source", out _));
        Assert.False(input.TryGetProperty("numReplicas", out _));
    }

    [Fact]
    public async Task ServiceInstanceLimitsUpdate_OmitsUnsetFields()
    {
        var handler = new RecordingHandler("""{"data":true}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.ServiceInstanceLimitsUpdateAsync(
            new ServiceInstanceLimitsUpdateInput
            {
                ServiceId = "svc_placeholder",
                EnvironmentId = "env_placeholder",
                MemoryGb = 2
            },
            "placeholder-token");

        using var document = System.Text.Json.JsonDocument.Parse(handler.Body);
        var input = document.RootElement.GetProperty("variables").GetProperty("input");
        Assert.Equal("svc_placeholder", input.GetProperty("serviceId").GetString());
        Assert.Equal("env_placeholder", input.GetProperty("environmentId").GetString());
        Assert.Equal(2, input.GetProperty("memoryGB").GetDouble());
        Assert.False(input.TryGetProperty("vCPUs", out _));
    }

    [Fact]
    public async Task SendAsync_Http400_SurfacesRailwayErrorText()
    {
        var handler = new RecordingHandler(
            """{"errors":[{"message":"Field \"templateId\" of required type \"String!\" was not provided."}]}""",
            HttpStatusCode.BadRequest);
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.TemplateAsync("postgres", "placeholder-token"));

        Assert.Contains("templateId", exception.Message, StringComparison.Ordinal);
        Assert.Contains("400", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyTemplateAsync_EmptySerializedConfig_DoesNotCallTemplateDeployV2()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("template", """{"data":{"template":{"id":"tpl_placeholder","code":"postgres","serializedConfig":{}}}}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ApplyTemplateAsync(
            "postgres",
            GraphQLFixtures.ProjectId,
            GraphQLFixtures.ProductionEnvironmentId,
            "placeholder-token"));

        Assert.Contains("serializedConfig", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Count("templateDeployV2"));
    }

    [Fact]
    public async Task ServiceDelete_PostsIdAndEnvironmentId()
    {
        var handler = new RecordingHandler(GraphQLFixtures.ServiceDelete);
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.ServiceDeleteAsync(
            GraphQLFixtures.ApiServiceId,
            GraphQLFixtures.ProductionEnvironmentId,
            "placeholder-token");

        Assert.Contains("\"operationName\":\"serviceDelete\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains(GraphQLFixtures.ApiServiceId, handler.Body, StringComparison.Ordinal);
        Assert.Contains("environmentId", handler.Body, StringComparison.Ordinal);
        Assert.Contains(GraphQLFixtures.ProductionEnvironmentId, handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("projectDelete", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("bucketDelete", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnvironmentDelete_PostsConfirmedMutation()
    {
        var handler = new RecordingHandler(GraphQLFixtures.EnvironmentDelete);
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.EnvironmentDeleteAsync(GraphQLFixtures.StagingEnvironmentId, "placeholder-token");

        Assert.Contains("\"operationName\":\"environmentDelete\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains(GraphQLFixtures.StagingEnvironmentId, handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("projectDelete", handler.Body, StringComparison.Ordinal);
    }

    private sealed class RecordingHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";
        public Uri? Uri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
