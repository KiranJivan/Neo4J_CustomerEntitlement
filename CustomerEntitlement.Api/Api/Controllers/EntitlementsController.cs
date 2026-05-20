using CustomerEntitlement.Api.Application;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CustomerEntitlement.Api.Controllers;

[ApiController]
[Route("api/entitlements")]
public sealed class EntitlementsController : ControllerBase
{
    private readonly IMediator _mediator;

    public EntitlementsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Evaluates whether a subject identity is entitled to perform
    /// a named action against a specific resource.
    /// </summary>
    /// <remarks>
    /// Example — allow (Kiran via Call Center):
    ///
    ///     POST /api/entitlements/evaluate
    ///     {
    ///       "subjectId":  "P-001",
    ///       "action":     "ServiceRequest",
    ///       "resourceId": "SRV-STOP",
    ///       "channelId":  "CH-CALL"
    ///     }
    ///
    /// Example — deny (Mark has no active profile):
    ///
    ///     POST /api/entitlements/evaluate
    ///     {
    ///       "subjectId":  "P-002",
    ///       "action":     "ServiceRequest",
    ///       "resourceId": "SRV-STOP"
    ///     }
    /// </remarks>
    [HttpPost("evaluate")]
    [ProducesResponseType(typeof(EntitlementResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Evaluate(
        [FromBody] EntitlementQuery query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.SubjectId)
            || string.IsNullOrWhiteSpace(query.Action)
            || string.IsNullOrWhiteSpace(query.ResourceId))
        {
            return BadRequest("SubjectId, Action, and ResourceId are required.");
        }

        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result);
    }
}
