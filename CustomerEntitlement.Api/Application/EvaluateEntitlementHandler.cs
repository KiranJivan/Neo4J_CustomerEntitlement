using MediatR;

namespace CustomerEntitlement.Api.Application;

public sealed class EvaluateEntitlementHandler : IRequestHandler<EntitlementQuery, EntitlementResult>
{
    private readonly IEntitlementRepository _repository;

    public EvaluateEntitlementHandler(IEntitlementRepository repository) => _repository = repository;

    public Task<EntitlementResult> Handle(EntitlementQuery request, CancellationToken cancellationToken)
        => _repository.EvaluateAsync(request, cancellationToken);
}
