namespace Aspire.Hosting.Railway.Tests;

public class RailwayBucketS3AddressingTests
{
    [Fact]
    public void BucketS3Endpoint_IsDocumentedT3Host()
    {
        Assert.Equal("https://t3.storageapi.dev", RailwayConstants.BucketS3Endpoint);
        Assert.Equal("t3.storageapi.dev", RailwayBucketS3Addressing.DocumentedHost);
        Assert.Equal("storage.railway.app", RailwayBucketS3Addressing.LegacyHost);
    }

    [Theory]
    [InlineData("https://t3.storageapi.dev")]
    [InlineData("https://T3.STORAGEAPI.DEV")]
    [InlineData("t3.storageapi.dev")]
    [InlineData(null)]
    [InlineData("")]
    public void DocumentedHost_IsVirtualHosted(string? endpoint)
    {
        Assert.True(RailwayBucketS3Addressing.IsVirtualHostedRailwayEndpoint(endpoint));
        Assert.False(RailwayBucketS3Addressing.ResolveForcePathStyle(endpoint));
    }

    [Theory]
    [InlineData("https://storage.railway.app")]
    [InlineData("https://STORAGE.RAILWAY.APP")]
    [InlineData("storage.railway.app")]
    public void LegacyHost_IsVirtualHosted(string endpoint)
    {
        Assert.True(RailwayBucketS3Addressing.IsVirtualHostedRailwayEndpoint(endpoint));
        Assert.False(RailwayBucketS3Addressing.ResolveForcePathStyle(endpoint));
    }

    [Fact]
    public void LocalEmulator_IsPathStyle()
    {
        Assert.False(RailwayBucketS3Addressing.IsVirtualHostedRailwayEndpoint("http://localhost:9090"));
        Assert.True(RailwayBucketS3Addressing.ResolveForcePathStyle("http://localhost:9090"));
    }

    [Theory]
    [InlineData("virtual", false)]
    [InlineData("VIRTUAL", false)]
    [InlineData("path", true)]
    [InlineData("PATH", true)]
    public void UrlStyle_MapsToForcePathStyle(string urlStyle, bool forcePathStyle)
    {
        Assert.Equal(forcePathStyle, RailwayBucketS3Addressing.ForcePathStyleFromUrlStyle(urlStyle));
        Assert.Equal(
            forcePathStyle,
            RailwayBucketS3Addressing.ResolveForcePathStyle("https://t3.storageapi.dev", urlStyle: urlStyle));
    }

    [Fact]
    public void UrlStyle_WinsOverHostnameHeuristic()
    {
        Assert.True(RailwayBucketS3Addressing.ResolveForcePathStyle(
            "https://t3.storageapi.dev",
            urlStyle: "path"));
        Assert.False(RailwayBucketS3Addressing.ResolveForcePathStyle(
            "http://localhost:9090",
            urlStyle: "virtual"));
    }

    [Fact]
    public void ExplicitForcePathStyle_WinsOverUrlStyle()
    {
        Assert.True(RailwayBucketS3Addressing.ResolveForcePathStyle(
            "https://t3.storageapi.dev",
            forcePathStyle: true,
            urlStyle: "virtual"));
    }

    [Fact]
    public void FormatConnectionString_IncludesUrlStyleWhenPresent()
    {
        var value = RailwayBucketS3Addressing.FormatConnectionString(
            "https://t3.storageapi.dev",
            "placeholder-access-key",
            "placeholder-secret-key",
            "uploads",
            "auto",
            forcePathStyle: false,
            urlStyle: "virtual");

        Assert.Equal(
            "Endpoint=https://t3.storageapi.dev;AccessKeyId=placeholder-access-key;SecretAccessKey=placeholder-secret-key;Bucket=uploads;Region=auto;ForcePathStyle=false;UrlStyle=virtual",
            value);
    }

    [Fact]
    public void FormatConnectionString_OmitsUrlStyleWhenUnset()
    {
        var value = RailwayBucketS3Addressing.FormatConnectionString(
            "https://storage.railway.app",
            "placeholder-access-key",
            "placeholder-secret-key",
            "uploads",
            "auto",
            forcePathStyle: false);

        Assert.Equal(
            "Endpoint=https://storage.railway.app;AccessKeyId=placeholder-access-key;SecretAccessKey=placeholder-secret-key;Bucket=uploads;Region=auto;ForcePathStyle=false",
            value);
        Assert.DoesNotContain("UrlStyle", value, StringComparison.Ordinal);
    }
}
