using Aspire.Hosting.Railway;

namespace Aspire.Hosting.Railway.Tests;

public sealed class RailwayRegionTests
{
    public static readonly TheoryData<RailwayRegion, string> OfficialMembers = new()
    {
        { RailwayRegion.UsWest2, "us-west2" },
        { RailwayRegion.UsEast4, "us-east4-eqdc4a" },
        { RailwayRegion.EuropeWest4, "europe-west4-drams3a" },
        { RailwayRegion.AsiaSoutheast1, "asia-southeast1-eqsg3a" }
    };

    [Theory]
    [MemberData(nameof(OfficialMembers))]
    public void ToRegionId_MapsOfficialMembers(RailwayRegion region, string expectedId)
    {
        Assert.Equal(expectedId, RailwayRegionMapper.ToRegionId(region));
        Assert.Contains(expectedId, RailwayConstants.OfficialRegionIds);
    }

    [Fact]
    public void OfficialRegionIds_AreExactlyTheFourDeployKeys()
    {
        Assert.Equal(
            [
                "us-west2",
                "us-east4-eqdc4a",
                "europe-west4-drams3a",
                "asia-southeast1-eqsg3a"
            ],
            RailwayConstants.OfficialRegionIds);
        Assert.Equal(Enum.GetValues<RailwayRegion>().Length, RailwayConstants.OfficialRegionIds.Count);
    }

    [Fact]
    public void ToOfficialReplicaRegions_UsesOfficialKeys()
    {
        var mapped = RailwayRegionMapper.ToOfficialReplicaRegions(new Dictionary<RailwayRegion, int>
        {
            [RailwayRegion.UsWest2] = 2,
            [RailwayRegion.EuropeWest4] = 1
        });

        Assert.Equal(2, mapped["us-west2"]);
        Assert.Equal(1, mapped["europe-west4-drams3a"]);
        Assert.DoesNotContain("sjc", mapped.Keys);
        Assert.DoesNotContain("us-west1", mapped.Keys);
        Assert.DoesNotContain("europe-west4", mapped.Keys);
    }

    [Theory]
    [InlineData("sjc")]
    [InlineData("iad")]
    [InlineData("ams")]
    [InlineData("sin")]
    [InlineData("us-west1")]
    [InlineData("us-east4")]
    [InlineData("europe-west4")]
    [InlineData("not-a-railway-region")]
    public void RequireOfficialRegionId_RejectsAirportCodesAndOldIds(string regionId)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayRegionMapper.RequireOfficialRegionId("api", regionId));

        Assert.Contains(regionId, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Airport codes", exception.Message, StringComparison.Ordinal);
        Assert.Contains("us-west2", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("deprecat", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
