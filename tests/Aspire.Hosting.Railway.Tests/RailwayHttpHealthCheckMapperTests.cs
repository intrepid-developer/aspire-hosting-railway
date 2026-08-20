namespace Aspire.Hosting.Railway.Tests;

public class RailwayHttpHealthCheckMapperTests
{
    [Theory]
    [InlineData("api", "api_http_/health_200_check", "/health")]
    [InlineData("api", "api_http_/_200_check", "/")]
    [InlineData("api", "api_https_/ready_204_check", "/ready")]
    [InlineData("api", "api_http_/health/live_200_check", "/health/live")]
    public void TryParseHttpHealthCheckKey_ReadsPathFromAspireKey(string resourceName, string key, string expected)
    {
        Assert.True(RailwayHttpHealthCheckMapper.TryParseHttpHealthCheckKey(resourceName, key, out var path));
        Assert.Equal(expected, path);
    }

    [Theory]
    [InlineData("api", "my-custom-check")]
    [InlineData("api", "other_http_/health_200_check")]
    [InlineData("api", "api_not-http")]
    public void TryParseHttpHealthCheckKey_IgnoresCustomKeys(string resourceName, string key)
    {
        Assert.False(RailwayHttpHealthCheckMapper.TryParseHttpHealthCheckKey(resourceName, key, out var path));
        Assert.Equal("", path);
    }

    [Fact]
    public void TryParseHttpHealthCheckKey_HttpShapedKeyWithoutPath_FailsHonestly()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RailwayHttpHealthCheckMapper.TryParseHttpHealthCheckKey("api", "api_http_health_200_check", out _));

        Assert.Contains("healthcheckPath", exception.Message, StringComparison.Ordinal);
        Assert.Contains("/health", exception.Message, StringComparison.Ordinal);
        Assert.Contains("WithHttpHealthCheck", exception.Message, StringComparison.Ordinal);
    }
}
