using CustomerEntitlement.Api.Domain;

namespace CustomerEntitlement.Api.Application;

/// <summary>
/// The canonical response from an entitlement evaluation.
/// IsAllowed   – true = allow, false = deny
/// Reason      – human-readable explanation of the decision
/// GrantedTerm – the Entitlement.name that permitted access (null on deny)
/// Details     – evaluation metadata for auditing and debugging
/// </summary>
public record EntitlementResult(
    bool IsAllowed,
    string Reason,
    string? GrantedTerm,
    EvaluationMetadata Details
);
