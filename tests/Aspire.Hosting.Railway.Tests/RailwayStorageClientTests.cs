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
                "Endpoint=https://storage.railway.app;AccessKeyId=placeholder-access-key;SecretAccessKey=placeholder-secret-key;Bucket=uploads;Region=auto;ForcePathStyle=false"
        });

        builder.AddRailwayBucketClient("uploads");

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<IAmazonS3>();
        var settings = host.Services.GetRequiredService<Aspire.Railway.Storage.RailwayBucketSettings>();

        Assert.NotNull(client);
        Assert.Equal("uploads", settings.BucketName);
        Assert.Same(client, host.Services.GetRequiredKeyedService<IAmazonS3>("uploads"));
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
}
