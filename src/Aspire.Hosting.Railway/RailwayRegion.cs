namespace Aspire.Hosting.Railway;

/// <summary>
/// Official Railway compute deploy regions (<c>Region.region</c>).
/// </summary>
/// <remarks>
/// Members map to the official deploy keys documented at
/// <see href="https://docs.railway.com/deployments/regions"/>, not airport
/// codes (<c>Query.regions.id</c>: sjc, iad, ams, sin) and not older ids
/// (us-west1, us-east4, europe-west4). GraphQL apply sends the mapped
/// <c>Region.region</c> string only.
/// </remarks>
public enum RailwayRegion
{
    /// <summary>US West — GraphQL deploy key <c>us-west2</c>.</summary>
    UsWest2,

    /// <summary>US East — GraphQL deploy key <c>us-east4-eqdc4a</c>.</summary>
    UsEast4,

    /// <summary>Europe West — GraphQL deploy key <c>europe-west4-drams3a</c>.</summary>
    EuropeWest4,

    /// <summary>Asia Southeast — GraphQL deploy key <c>asia-southeast1-eqsg3a</c>.</summary>
    AsiaSoutheast1
}
