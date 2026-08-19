using Aspire.Hosting.ApplicationModel;
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
}
