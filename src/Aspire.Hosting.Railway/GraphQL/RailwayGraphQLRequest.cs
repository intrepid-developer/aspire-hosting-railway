using System.Text.Json.Serialization;

namespace Aspire.Hosting.Railway;

/// <summary>
/// JSON body posted to Railway GraphQL.
/// </summary>
public sealed class RailwayGraphQLRequest
{
    /// <summary>
    /// Gets or sets the GraphQL query or mutation document.
    /// </summary>
    [JsonPropertyName("query")]
    public required string Query { get; init; }

    /// <summary>
    /// Gets or sets the operation name, when the document contains more than one operation.
    /// </summary>
    [JsonPropertyName("operationName")]
    public string? OperationName { get; init; }

    /// <summary>
    /// Gets or sets the variables object.
    /// </summary>
    [JsonPropertyName("variables")]
    public object? Variables { get; init; }
}

/// <summary>
/// Envelope returned by Railway GraphQL.
/// </summary>
/// <typeparam name="T">The <c>data</c> payload type.</typeparam>
public sealed class RailwayGraphQLResponse<T>
{
    /// <summary>
    /// Gets or sets the data payload.
    /// </summary>
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    /// <summary>
    /// Gets or sets GraphQL errors, if any.
    /// </summary>
    [JsonPropertyName("errors")]
    public List<RailwayGraphQLError>? Errors { get; set; }
}

/// <summary>
/// A GraphQL error object.
/// </summary>
public sealed class RailwayGraphQLError
{
    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
