namespace CustomerEntitlement.Api.Domain;

/// <summary>
/// Diagnostic context returned alongside every entitlement decision.
/// Tells the caller which profile was evaluated and whether the
/// optional channel constraint was checked and passed.
/// </summary>
public record EvaluationMetadata(string? ProfileId, bool ChannelValidated);
