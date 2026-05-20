using CustomerEntitlement.Api.Application;
using CustomerEntitlement.Api.Controllers;
using CustomerEntitlement.Api.Domain;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace CustomerEntitlement.Tests;

/// <summary>
/// Unit tests for the entitlement check path.
/// The repository is mocked so tests run without a live Neo4j connection.
/// Each test drives a specific denial branch or the grant path through the handler.
/// </summary>
public class EntitlementCheckTests
{
    private readonly Mock<IEntitlementRepository> _repoMock = new();
    private readonly Mock<IMediator> _mediatorMock = new();

    private EvaluateEntitlementHandler Handler => new(_repoMock.Object);
    private EntitlementsController Controller => new(_mediatorMock.Object);

    // Handler tests — verify traversal logic per graph step

    [Fact]
    public async Task Handler_GrantedPath_ReturnsAllowedWithEntitlementName()
    {
        // Kiran (P-001) requests ServiceRequest on SRV-STOP via Call Center
        var query = new EntitlementQuery("P-001", "ServiceRequest", "SRV-STOP", "CH-CALL");
        var expected = Allow("CallCenterServicing", "PROF-001", channelValidated: true);

        _repoMock.Setup(r => r.EvaluateAsync(query, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(expected);

        var result = await Handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsAllowed);
        Assert.Equal("CallCenterServicing", result.GrantedTerm);
        Assert.Equal("Access granted.", result.Reason);
        Assert.Equal("PROF-001", result.Details.ProfileId);
        Assert.True(result.Details.ChannelValidated);
    }

    [Fact]
    public async Task Handler_GrantedPath_NoChannel_ChannelValidatedIsFalse()
    {
        // No channelId supplied — still granted, but ChannelValidated should be false
        var query = new EntitlementQuery("P-001", "ServiceRequest", "SRV-STOP");
        var expected = Allow("CallCenterServicing", "PROF-001", channelValidated: false);

        _repoMock.Setup(r => r.EvaluateAsync(query, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(expected);

        var result = await Handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsAllowed);
        Assert.False(result.Details.ChannelValidated);
    }

    [Fact]
    public async Task Handler_SubjectNotFound_ReturnsDenyWithNullProfile()
    {
        var query = new EntitlementQuery("P-GHOST", "ServiceRequest", "SRV-STOP");
        var expected = Deny("Subject 'P-GHOST' not found.", null);

        _repoMock.Setup(r => r.EvaluateAsync(query, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(expected);

        var result = await Handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Null(result.GrantedTerm);
        Assert.Null(result.Details.ProfileId);
        Assert.Contains("not found", result.Reason);
    }

    [Fact]
    public async Task Handler_InactiveProfile_ReturnsDenyMentioningStatus()
    {
        // Mark (P-002) has a Pending profile — no traversal should succeed
        var query = new EntitlementQuery("P-002", "ServiceRequest", "SRV-STOP");
        var expected = Deny("Profile 'PROF-002' is not active (status: Pending).", "PROF-002");

        _repoMock.Setup(r => r.EvaluateAsync(query, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(expected);

        var result = await Handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Contains("not active", result.Reason);
        Assert.Equal("PROF-002", result.Details.ProfileId);
    }

    [Fact]
    public async Task Handler_ActionNotOnProfile_ReturnsDenyMentioningAction()
    {
        var query = new EntitlementQuery("P-001", "UnknownAction", "SRV-STOP");
        var expected = Deny("No entitlement found for action 'UnknownAction' on profile 'PROF-001'.", "PROF-001");

        _repoMock.Setup(r => r.EvaluateAsync(query, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(expected);

        var result = await Handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Contains("UnknownAction", result.Reason);
    }

    [Fact]
    public async Task Handler_ResourceNotAccessible_ReturnsDenyMentioningResource()
    {
        // ExecuteTransfer does not cover SRV-STOP
        var query = new EntitlementQuery("P-001", "ExecuteTransfer", "SRV-STOP");
        var expected = Deny("Action 'ExecuteTransfer' does not grant access to resource 'SRV-STOP'.", "PROF-001");

        _repoMock.Setup(r => r.EvaluateAsync(query, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(expected);

        var result = await Handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Contains("SRV-STOP", result.Reason);
    }

    [Fact]
    public async Task Handler_WrongChannel_ReturnsDenyMentioningChannel()
    {
        // ServiceRequest is Call Center only; requesting via Mobile App should deny
        var query = new EntitlementQuery("P-001", "ServiceRequest", "SRV-STOP", "CH-MOB");
        var expected = Deny(
            "Action 'ServiceRequest' is not permitted via channel 'CH-MOB'. Allowed: [CH-CALL].",
            "PROF-001");

        _repoMock.Setup(r => r.EvaluateAsync(query, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(expected);

        var result = await Handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Contains("CH-MOB", result.Reason);
        Assert.Contains("CH-CALL", result.Reason);
    }

    // Controller tests — verify HTTP contract

    [Fact]
    public async Task Controller_ValidQuery_Returns200WithResult()
    {
        var query = new EntitlementQuery("P-001", "ServiceRequest", "SRV-STOP");
        var expected = Allow("CallCenterServicing", "PROF-001", channelValidated: false);

        _mediatorMock.Setup(m => m.Send(query, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(expected);

        var actionResult = await Controller.Evaluate(query, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        var payload = Assert.IsType<EntitlementResult>(ok.Value);
        Assert.True(payload.IsAllowed);
    }

    [Fact]
    public async Task Controller_MissingSubjectId_Returns400()
    {
        var query = new EntitlementQuery("", "ServiceRequest", "SRV-STOP");

        var actionResult = await Controller.Evaluate(query, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(actionResult);
    }

    [Fact]
    public async Task Controller_MissingAction_Returns400()
    {
        var query = new EntitlementQuery("P-001", "   ", "SRV-STOP");

        var actionResult = await Controller.Evaluate(query, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(actionResult);
    }

    [Fact]
    public async Task Controller_MissingResourceId_Returns400()
    {
        var query = new EntitlementQuery("P-001", "ServiceRequest", "");

        var actionResult = await Controller.Evaluate(query, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(actionResult);
    }

    // Helpers

    private static EntitlementResult Allow(string grantedTerm, string profileId, bool channelValidated) =>
        new(true, "Access granted.", grantedTerm, new EvaluationMetadata(profileId, channelValidated));

    private static EntitlementResult Deny(string reason, string? profileId) =>
        new(false, reason, null, new EvaluationMetadata(profileId, false));
}
