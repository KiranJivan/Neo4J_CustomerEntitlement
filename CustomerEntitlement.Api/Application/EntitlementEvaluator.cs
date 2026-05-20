using CustomerEntitlement.Api.Domain;

namespace CustomerEntitlement.Api.Application;

/// <summary>
/// Raw data returned by the Cypher traversal, mapped out of IRecord
/// before any business logic runs. Keeping this as a plain record
/// lets it be constructed in tests without any Neo4j dependency.
/// </summary>
public record EvaluationRow(
    bool PartyFound,
    bool ProfileFound,
    string? ProfileStatus,
    string? ProfileId,
    bool EntitlementFound,
    bool ResourceFound,
    IReadOnlyList<string> AllowedChannels,
    string? EntitlementName);

/// <summary>
/// Pure business logic: walks the BIAN traversal result step by step
/// and returns a structured allow/deny decision with a precise reason.
///
/// Denial ladder (matches the graph traversal order):
///   1. Subject not found
///   2. No Customer Access Profile
///   3. Profile not Active
///   4. No matching Entitlement for the action
///   5. Resource not accessible via that Entitlement
///   6. Channel not permitted (when channelId is supplied)
///   7. Access granted
/// </summary>
public static class EntitlementEvaluator
{
    public static EntitlementResult Evaluate(EntitlementQuery query, EvaluationRow row)
    {
        if (!row.PartyFound)
            return Deny($"Subject '{query.SubjectId}' not found.", null);

        if (!row.ProfileFound)
            return Deny($"No Customer Access Profile found for subject '{query.SubjectId}'.", null);

        if (row.ProfileStatus != "Active")
            return Deny(
                $"Profile '{row.ProfileId}' is not active (status: {row.ProfileStatus ?? "unknown"}).",
                row.ProfileId);

        if (!row.EntitlementFound)
            return Deny(
                $"No entitlement found for action '{query.Action}' on profile '{row.ProfileId}'.",
                row.ProfileId);

        if (!row.ResourceFound)
            return Deny(
                $"Action '{query.Action}' does not grant access to resource '{query.ResourceId}'.",
                row.ProfileId);

        var channelValidated = false;
        if (query.ChannelId is not null)
        {
            if (!row.AllowedChannels.Contains(query.ChannelId))
                return Deny(
                    $"Action '{query.Action}' is not permitted via channel '{query.ChannelId}'. " +
                    $"Allowed: [{string.Join(", ", row.AllowedChannels)}].",
                    row.ProfileId);

            channelValidated = true;
        }

        return new EntitlementResult(
            IsAllowed:   true,
            Reason:      "Access granted.",
            GrantedTerm: row.EntitlementName,
            Details:     new EvaluationMetadata(row.ProfileId, channelValidated));
    }

    private static EntitlementResult Deny(string reason, string? profileId) =>
        new(false, reason, null, new EvaluationMetadata(profileId, false));
}
