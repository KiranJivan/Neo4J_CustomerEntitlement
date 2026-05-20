using MediatR;

namespace CustomerEntitlement.Api.Application;

/// <summary>
/// CQRS query that carries all inputs needed for a single entitlement decision.
/// SubjectId  – the Party.id of the identity being evaluated (e.g. "P-001")
/// Action     – the permission name to check (e.g. "ServiceRequest", "ExecuteTransfer")
/// ResourceId – the target node id (e.g. "SRV-STOP", "ACC-1001")
/// ChannelId  – optional; when provided, the entitlement must also be permitted via this channel
/// </summary>
public record EntitlementQuery(
    string SubjectId,
    string Action,
    string ResourceId,
    string? ChannelId = null
) : IRequest<EntitlementResult>;
