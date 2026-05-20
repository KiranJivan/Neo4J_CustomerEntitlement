namespace CustomerEntitlement.Api.Application;

public interface IEntitlementRepository
{
    Task<EntitlementResult> EvaluateAsync(EntitlementQuery query, CancellationToken cancellationToken = default);
}
