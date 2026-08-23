using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Railway;
using Aspire.Hosting.Railway.Storage;

namespace Aspire.Hosting.Railway.Tests;

public class RailwayBucketTests
{
    [Fact]
    public void AddRailwayBucket_RunMode_HealthCheckTargetsS3Endpoint()
    {
        var builder = TestAppBuilder.CreateRun();
        var uploads = builder.AddRailwayBucket("uploads");

        using var app = builder.Build();
        var model = TestAppBuilder.GetModel(app);
        var emulator = Assert.Single(model.Resources, resource => resource.Name == "uploads-s3");

        Assert.Contains(emulator.Annotations.OfType<EndpointAnnotation>(), endpoint => endpoint.Name == "s3");
        Assert.Same(emulator, uploads.Resource.Emulator);
        Assert.NotNull(uploads.Resource.EmulatorEndpoint);
        Assert.IsAssignableFrom<IResourceWithoutLifetime>(uploads.Resource);
    }

    [Fact]
    public void PublishMode_ConnectionString_IsPlaceholderNotServiceVariable()
    {
        var builder = TestAppBuilder.CreatePublish();
        var uploads = builder.AddRailwayBucket("uploads", configure: bucket => bucket.Region = RailwayBucketRegion.Ams);

        using var app = builder.Build();
        var resource = uploads.Resource;

        Assert.Null(resource.EmulatorEndpoint);
        Assert.Equal(
            RailwayReferenceExpressions.BucketConnectionPlaceholder("uploads"),
            resource.ConnectionStringExpression.ValueExpression);

        var properties = resource.GetConnectionProperties()
            .ToDictionary(pair => pair.Key, pair => pair.Value.ValueExpression, StringComparer.Ordinal);
        Assert.Equal("uploads", properties["Bucket"]);
        Assert.Equal("ams", properties["Region"]);
        Assert.Equal("false", properties["ForcePathStyle"]);
        Assert.False(properties.ContainsKey("Endpoint"));
        Assert.False(properties.ContainsKey("AccessKeyId"));
        Assert.False(properties.ContainsKey("SecretAccessKey"));
        Assert.All(properties.Values, value =>
        {
            Assert.DoesNotContain("${{", value, StringComparison.Ordinal);
            Assert.DoesNotContain("placeholder-access-key", value, StringComparison.Ordinal);
            Assert.DoesNotContain("placeholder-secret-key", value, StringComparison.Ordinal);
        });
    }
}
