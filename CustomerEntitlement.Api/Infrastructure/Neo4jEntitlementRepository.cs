using CustomerEntitlement.Api.Application;
using Neo4j.Driver;

namespace CustomerEntitlement.Api.Infrastructure;

/// <summary>
/// Runs the BIAN graph traversal via Neo4j and delegates the decision
/// to <see cref="EntitlementEvaluator.Evaluate"/>.
///
/// Traversal:
///   Party -[:OWNS]-> CustomerAccessProfile
///     -[:HAS_ENTITLEMENT]-> Entitlement {action}
///       -[:GRANTS_ACCESS]-> Resource {id}
///       -[:VIA_CHANNEL]-> Channel (optional constraint)
///
/// Each OPTIONAL MATCH step cascades nulls so C# receives structured
/// "how far did the path resolve?" data rather than raw graph data.
/// </summary>
public sealed class Neo4jEntitlementRepository : IEntitlementRepository
{
    private readonly IDriver _driver;
    private readonly string _database;

    private const string EvaluateCypher = """
        OPTIONAL MATCH (party:Party {id: $subjectId})

        WITH party
        OPTIONAL MATCH (party)-[:OWNS]->(profile:CustomerAccessProfile)

        WITH party, profile
        OPTIONAL MATCH (profile)-[:HAS_ENTITLEMENT]->(ent:Entitlement {action: $action})
            WHERE profile.status = 'Active'

        WITH party, profile, ent
        OPTIONAL MATCH (ent)-[:GRANTS_ACCESS]->(res)
            WHERE res.id = $resourceId

        WITH party, profile, ent, res
        OPTIONAL MATCH (ent)-[:VIA_CHANNEL]->(ch:Channel)
        WITH party, profile, ent, res, collect(ch.id) AS allowedChannels

        RETURN
            party IS NOT NULL           AS partyFound,
            profile IS NOT NULL         AS profileFound,
            profile.status              AS profileStatus,
            profile.id                  AS profileId,
            ent IS NOT NULL             AS entitlementFound,
            res IS NOT NULL             AS resourceFound,
            allowedChannels,
            ent.name                    AS entitlementName
        ORDER BY
            CASE WHEN res IS NOT NULL THEN 0
                 WHEN ent IS NOT NULL THEN 1
                 ELSE 2 END
        LIMIT 1
        """;

    public Neo4jEntitlementRepository(IDriver driver, IConfiguration configuration)
    {
        _driver = driver;
        _database = configuration["Neo4j:Database"] ?? "neo4j";
    }

    public async Task<EntitlementResult> EvaluateAsync(
        EntitlementQuery query,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession(o => o.WithDatabase(_database));

        var record = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(EvaluateCypher, new
            {
                subjectId  = query.SubjectId,
                action     = query.Action,
                resourceId = query.ResourceId
            });
            return await cursor.SingleAsync();
        });

        return EntitlementEvaluator.Evaluate(query, MapToRow(record));
    }

    // Maps a raw IRecord from the Cypher query into the typed EvaluationRow DTO.
    internal static EvaluationRow MapToRow(IRecord record) => new(
        PartyFound:       record["partyFound"].As<bool>(),
        ProfileFound:     record["profileFound"].As<bool>(),
        ProfileStatus:    record["profileStatus"].As<string?>(),
        ProfileId:        record["profileId"].As<string?>(),
        EntitlementFound: record["entitlementFound"].As<bool>(),
        ResourceFound:    record["resourceFound"].As<bool>(),
        AllowedChannels:  record["allowedChannels"].As<List<string>>(),
        EntitlementName:  record["entitlementName"].As<string?>());
}
