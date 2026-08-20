var builder = WebApplication.CreateBuilder(args);

builder.AddNpgsqlDataSource("postgres");
builder.AddRedisClient("redis");
builder.AddRailwayBucketClient("uploads");

var app = builder.Build();

app.MapGet("/", () => "ok");
app.MapGet("/health", () => "ok");

app.Run();
