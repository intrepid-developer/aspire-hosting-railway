using Amazon.S3;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aspire.Hosting.Railway.Tests;

public class RailwayStorageClientTests
{
    [Fact]
    public void AddRailwayBucketClient_RegistersIAmazonS3FromPlaceholderConnectionString()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:uploads"] =
                "Endpoint=https://t3.storageapi.dev;AccessKeyId=placeholder-access-key;SecretAccessKey=placeholder-secret-key;Bucket=uploads;Region=auto;ForcePathStyle=false"
        });

        builder.AddRailwayBucketClient("uploads");

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<IAmazonS3>();
        var settings = host.Services.GetRequiredService<Aspire.Railway.Storage.RailwayBucketSettings>();

        Assert.NotNull(client);
        Assert.Equal("uploads", settings.BucketName);
        Assert.Same(client, host.Services.GetRequiredKeyedService<IAmazonS3>("uploads"));
        var s3 = Assert.IsType<AmazonS3Client>(client);
        var config = Assert.IsType<AmazonS3Config>(s3.Config);
        AssertServiceUrl(config, "https://t3.storageapi.dev");
        Assert.False(config.ForcePathStyle);
    }

    [Fact]
    public void Parse_ReadsDocumentedConnectionStringFormat()
    {
        var options = Aspire.Railway.Storage.RailwayBucketConnectionOptions.Parse(
            "Endpoint=http://localhost:9090;AccessKeyId=s3mock;SecretAccessKey=s3mock;Bucket=uploads;Region=us-east-1;ForcePathStyle=true");

        Assert.Equal("http://localhost:9090", options.Endpoint);
        Assert.Equal("s3mock", options.AccessKeyId);
        Assert.Equal("s3mock", options.SecretAccessKey);
        Assert.Equal("uploads", options.Bucket);
        Assert.Equal("us-east-1", options.Region);
        Assert.True(options.ForcePathStyle);
    }

    [Fact]
    public void Parse_ReadsOptionalUrlStyle()
    {
        var options = Aspire.Railway.Storage.RailwayBucketConnectionOptions.Parse(
            "Endpoint=https://t3.storageapi.dev;AccessKeyId=placeholder-access-key;SecretAccessKey=placeholder-secret-key;Bucket=uploads;Region=auto;UrlStyle=virtual");

        Assert.Equal("https://t3.storageapi.dev", options.Endpoint);
        Assert.Equal("virtual", options.UrlStyle);
        Assert.Null(options.ForcePathStyle);
    }

    [Fact]
    public void CreateClient_DocumentedT3Host_IsVirtualHosted()
    {
        var config = CreateConfig("Endpoint=https://t3.storageapi.dev;AccessKeyId=placeholder-access-key;SecretAccessKey=placeholder-secret-key;Bucket=uploads;Region=auto");

        AssertServiceUrl(config, "https://t3.storageapi.dev");
        Assert.False(config.ForcePathStyle);
    }

    [Fact]
    public void CreateClient_LegacyStorageRailwayAppHost_IsVirtualHosted()
    {
        var config = CreateConfig("Endpoint=https://storage.railway.app;AccessKeyId=placeholder-access-key;SecretAccessKey=placeholder-secret-key;Bucket=uploads;Region=auto");

        AssertServiceUrl(config, "https://storage.railway.app");
        Assert.False(config.ForcePathStyle);
    }

    [Fact]
    public void CreateClient_LocalEmulatorHost_IsPathStyle()
    {
        var config = CreateConfig("Endpoint=http://localhost:9090;AccessKeyId=s3mock;SecretAccessKey=s3mock;Bucket=uploads;Region=us-east-1");

        Assert.True(config.ForcePathStyle);
    }

    [Fact]
    public void CreateClient_UrlStylePath_WinsOverT3HostHeuristic()
    {
        var config = CreateConfig("Endpoint=https://t3.storageapi.dev;AccessKeyId=placeholder-access-key;SecretAccessKey=placeholder-secret-key;Bucket=uploads;Region=auto;UrlStyle=path");

        Assert.True(config.ForcePathStyle);
    }

    [Fact]
    public void CreateClient_UrlStyleVirtual_WinsOverLocalHostHeuristic()
    {
        var config = CreateConfig("Endpoint=http://localhost:9090;AccessKeyId=s3mock;SecretAccessKey=s3mock;Bucket=uploads;Region=us-east-1;UrlStyle=virtual");

        Assert.False(config.ForcePathStyle);
    }

    [Fact]
    public void CreateClient_ExplicitForcePathStyle_WinsOverUrlStyle()
    {
        var config = CreateConfig("Endpoint=https://t3.storageapi.dev;AccessKeyId=placeholder-access-key;SecretAccessKey=placeholder-secret-key;Bucket=uploads;Region=auto;UrlStyle=virtual;ForcePathStyle=true");

        Assert.True(config.ForcePathStyle);
    }

    [Fact]
    public void CreateClient_MissingEndpoint_FallsBackToDocumentedT3Host()
    {
        var config = CreateConfig("AccessKeyId=placeholder-access-key;SecretAccessKey=placeholder-secret-key;Bucket=uploads;Region=auto");

        AssertServiceUrl(config, "https://t3.storageapi.dev");
        Assert.False(config.ForcePathStyle);
    }

    private static void AssertServiceUrl(AmazonS3Config config, string expected) =>
        Assert.Equal(expected.TrimEnd('/'), config.ServiceURL?.TrimEnd('/'));

    private static AmazonS3Config CreateConfig(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:uploads"] = connectionString
            })
            .Build();

        var client = Assert.IsType<AmazonS3Client>(
            Microsoft.Extensions.Hosting.RailwayBucketClientExtensions.CreateClient(configuration, "uploads"));
        return Assert.IsType<AmazonS3Config>(client.Config);
    }
}
