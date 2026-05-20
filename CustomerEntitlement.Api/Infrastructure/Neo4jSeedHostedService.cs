using Neo4j.Driver;

namespace CustomerEntitlement.Api.Infrastructure;

/// <summary>
/// Runs once at startup. If the target database is empty, loads the BIAN
/// Customer Access Entitlement demo graph so the API works out-of-the-box.
///
/// Demo graph layout:
///   Kiran (P-001, Active)  — fully connected: can ServiceRequest and ExecuteTransfer
///   Mark  (P-002, Pending) — party and profile exist, but no edges (intentionally isolated)
/// </summary>
public sealed class Neo4jSeedHostedService : IHostedService
{
    private readonly IDriver _driver;
    private readonly ILogger<Neo4jSeedHostedService> _logger;
    private readonly string _database;

    public Neo4jSeedHostedService(
        IDriver driver,
        ILogger<Neo4jSeedHostedService> logger,
        IConfiguration configuration)
    {
        _driver = driver;
        _logger = logger;
        _database = configuration["Neo4j:Database"] ?? "neo4j";
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_database));

            var nodeCount = await session.ExecuteReadAsync(async tx =>
            {
                var cursor = await tx.RunAsync("MATCH (n) RETURN count(n) AS total");
                var record = await cursor.SingleAsync();
                return record["total"].As<long>();
            });

            if (nodeCount > 0)
            {
                _logger.LogInformation(
                    "AuraDB already contains {Count} nodes. Skipping seed.", nodeCount);
                return;
            }

            _logger.LogInformation("Database is empty — seeding BIAN demo graph...");
            await session.ExecuteWriteAsync(async tx => { await tx.RunAsync(SeedCypher); });
            _logger.LogInformation("Seed complete.");
        }
        catch (Exception ex)
        {
            // Log but don't crash the host — the API can still serve requests
            // against a pre-populated database even if the seed check fails.
            _logger.LogError(ex, "Seed check failed. Verify Neo4j connectivity.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Seed Cypher — BIAN Customer Access Entitlement demo graph.
    private const string SeedCypher = """
        // Identities
        CREATE (kiran:Party {id: 'P-001', type: 'Person', name: 'Kiran Jivan'})
        CREATE (kiranProfile:CustomerAccessProfile {id: 'PROF-001', status: 'Active'})

        // Mark is intentionally isolated: party + profile exist but no edges.
        CREATE (mark:Party {id: 'P-002', type: 'Person', name: 'Mark'})
        CREATE (markProfile:CustomerAccessProfile {id: 'PROF-002', status: 'Pending'})

        // Entitlements (permission rules)
        CREATE (callCenterRule:Entitlement  {id: 'ENT-01', name: 'CallCenterServicing', action: 'ServiceRequest'})
        CREATE (mobileTransferRule:Entitlement {id: 'ENT-02', name: 'MobileTransfer',   action: 'ExecuteTransfer'})

        // Utilization limit (path-only; no runtime amount check in this MVP)
        CREATE (atmLimit:UtilizationLimit {id: 'LIM-01', metric: 'Amount', value: 500, period: '24h', currency: 'NZD'})

        // Access channels
        CREATE (callCenter:Channel {id: 'CH-CALL', name: 'Call Center'})
        CREATE (atmNet:Channel     {id: 'CH-ATM',  name: 'ATM Network'})
        CREATE (mobileApp:Channel  {id: 'CH-MOB',  name: 'Mobile App'})

        // Resources
        CREATE (stopPayment:Service         {id: 'SRV-STOP',  name: 'Stop Payment'})
        CREATE (checking:ProductInstance    {id: 'ACC-1001',  type: 'Checking'})
        CREATE (savings:ProductInstance     {id: 'ACC-2002',  type: 'Savings'})

        // ---- Edges (Kiran only; Mark intentionally gets no edges) ----

        // Party owns Profile
        CREATE (kiran)-[:OWNS]->(kiranProfile)

        // Profile holds entitlements and limits
        CREATE (kiranProfile)-[:HAS_ENTITLEMENT]->(callCenterRule)
        CREATE (kiranProfile)-[:HAS_ENTITLEMENT]->(mobileTransferRule)
        CREATE (kiranProfile)-[:HAS_LIMIT]->(atmLimit)

        // Entitlements are restricted to specific channels
        CREATE (callCenterRule)-[:VIA_CHANNEL]->(callCenter)
        CREATE (mobileTransferRule)-[:VIA_CHANNEL]->(mobileApp)
        CREATE (atmLimit)-[:VIA_CHANNEL]->(atmNet)

        // Entitlements grant access to specific resources
        CREATE (callCenterRule)-[:GRANTS_ACCESS]->(stopPayment)
        CREATE (callCenterRule)-[:GRANTS_ACCESS]->(checking)
        CREATE (mobileTransferRule)-[:GRANTS_ACCESS]->(checking)
        CREATE (mobileTransferRule)-[:GRANTS_ACCESS]->(savings)

        // Limit constrains multiple product accounts (cross-product limit)
        CREATE (atmLimit)-[:CONSTRAINS]->(checking)
        CREATE (atmLimit)-[:CONSTRAINS]->(savings)
        """;
}
