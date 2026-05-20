using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using CustomerEntitlement.Api.Application;
using CustomerEntitlement.Api.Infrastructure;
using Neo4j.Driver;

var builder = WebApplication.CreateBuilder(args);

// Neo4j — single IDriver instance manages the internal connection pool.
builder.Services.AddSingleton<IDriver>(sp => Program.CreateNeo4jDriver(builder.Configuration));

// MediatR CQRS
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblyContaining<EvaluateEntitlementHandler>());

builder.Services.AddScoped<IEntitlementRepository, Neo4jEntitlementRepository>();

// Seed demo data on first startup if the database is empty.
builder.Services.AddHostedService<Neo4jSeedHostedService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title       = "Customer Entitlement API",
        Version     = "v1",
        Description = "BIAN-aligned graph-backed entitlement check service."
    });
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Customer Entitlement API v1"));

app.MapControllers();
app.Run();

// Partial class required for WebApplicationFactory in tests.
public partial class Program
{
    [ExcludeFromCodeCoverage]
    internal static IDriver CreateNeo4jDriver(IConfiguration cfg)
    {
        var neo4j = cfg.GetSection("Neo4j");
        return GraphDatabase.Driver(
            neo4j["Uri"]      ?? throw new InvalidOperationException("Neo4j:Uri is required in appsettings."),
            AuthTokens.Basic(
                neo4j["Username"] ?? throw new InvalidOperationException("Neo4j:Username is required."),
                neo4j["Password"] ?? throw new InvalidOperationException("Neo4j:Password is required.")));
    }
}
