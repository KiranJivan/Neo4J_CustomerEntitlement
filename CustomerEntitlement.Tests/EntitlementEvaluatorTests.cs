using CustomerEntitlement.Api.Application;

namespace CustomerEntitlement.Tests;

public class EntitlementEvaluatorTests
{
    private static readonly EntitlementQuery BaseQuery =
        new("P-001", "ServiceRequest", "SRV-STOP", "CH-CALL");

    // Denial branch 1: party not in the graph
    [Fact]
    public void Evaluate_PartyNotFound_ReturnsDeny()
    {
        var row = BuildRow(partyFound: false);

        var result = EntitlementEvaluator.Evaluate(BaseQuery, row);

        Assert.False(result.IsAllowed);
        Assert.Null(result.GrantedTerm);
        Assert.Null(result.Details.ProfileId);
        Assert.Contains("P-001", result.Reason);
        Assert.Contains("not found", result.Reason);
    }

    // Denial branch 2: party exists but has no OWNS edge to a profile
    [Fact]
    public void Evaluate_ProfileNotFound_ReturnsDeny()
    {
        var row = BuildRow(profileFound: false);

        var result = EntitlementEvaluator.Evaluate(BaseQuery, row);

        Assert.False(result.IsAllowed);
        Assert.Null(result.Details.ProfileId);
        Assert.Contains("No Customer Access Profile", result.Reason);
    }

    // Denial branch 3: profile exists but status != Active
    [Fact]
    public void Evaluate_ProfileNotActive_ReturnsDenyWithStatus()
    {
        var row = BuildRow(profileStatus: "Pending", profileId: "PROF-002");

        var result = EntitlementEvaluator.Evaluate(BaseQuery, row);

        Assert.False(result.IsAllowed);
        Assert.Equal("PROF-002", result.Details.ProfileId);
        Assert.Contains("not active", result.Reason);
        Assert.Contains("Pending", result.Reason);
    }

    [Fact]
    public void Evaluate_ProfileStatusNull_ReturnsDenyWithUnknown()
    {
        var row = BuildRow(profileStatus: null, profileId: "PROF-X");

        var result = EntitlementEvaluator.Evaluate(BaseQuery, row);

        Assert.False(result.IsAllowed);
        Assert.Contains("unknown", result.Reason);
    }

    // Denial branch 4: no entitlement matches the requested action
    [Fact]
    public void Evaluate_EntitlementNotFound_ReturnsDeny()
    {
        var row = BuildRow(entitlementFound: false);

        var result = EntitlementEvaluator.Evaluate(BaseQuery, row);

        Assert.False(result.IsAllowed);
        Assert.Contains("ServiceRequest", result.Reason);
        Assert.Contains("No entitlement", result.Reason);
    }

    // Denial branch 5: entitlement exists but does not cover the resource
    [Fact]
    public void Evaluate_ResourceNotFound_ReturnsDeny()
    {
        var row = BuildRow(resourceFound: false);

        var result = EntitlementEvaluator.Evaluate(BaseQuery, row);

        Assert.False(result.IsAllowed);
        Assert.Contains("SRV-STOP", result.Reason);
        Assert.Contains("does not grant", result.Reason);
    }

    // Denial branch 6: channel supplied but not in the allowed set
    [Fact]
    public void Evaluate_WrongChannel_ReturnsDenyListingAllowed()
    {
        var row = BuildRow(allowedChannels: ["CH-CALL"]);
        var query = BaseQuery with { ChannelId = "CH-MOB" };

        var result = EntitlementEvaluator.Evaluate(query, row);

        Assert.False(result.IsAllowed);
        Assert.False(result.Details.ChannelValidated);
        Assert.Contains("CH-MOB", result.Reason);
        Assert.Contains("CH-CALL", result.Reason);
    }

    // Grant path — with channel provided
    [Fact]
    public void Evaluate_AllConditionsMet_WithChannel_ReturnsAllow()
    {
        var row = BuildRow(allowedChannels: ["CH-CALL"], entitlementName: "CallCenterServicing");

        var result = EntitlementEvaluator.Evaluate(BaseQuery, row);

        Assert.True(result.IsAllowed);
        Assert.Equal("Access granted.", result.Reason);
        Assert.Equal("CallCenterServicing", result.GrantedTerm);
        Assert.Equal("PROF-001", result.Details.ProfileId);
        Assert.True(result.Details.ChannelValidated);
    }

    // Grant path — no channel supplied (channel check skipped)
    [Fact]
    public void Evaluate_AllConditionsMet_NoChannel_ReturnsAllowWithoutChannelValidated()
    {
        var row = BuildRow(entitlementName: "CallCenterServicing");
        var query = BaseQuery with { ChannelId = null };

        var result = EntitlementEvaluator.Evaluate(query, row);

        Assert.True(result.IsAllowed);
        Assert.Equal("CallCenterServicing", result.GrantedTerm);
        Assert.False(result.Details.ChannelValidated);
    }

    // Builds an EvaluationRow with all fields defaulting to a fully-granted state.
    private static EvaluationRow BuildRow(
        bool partyFound         = true,
        bool profileFound       = true,
        string? profileStatus   = "Active",
        string? profileId       = "PROF-001",
        bool entitlementFound   = true,
        bool resourceFound      = true,
        IReadOnlyList<string>? allowedChannels = null,
        string? entitlementName = "CallCenterServicing")
    {
        return new EvaluationRow(
            PartyFound:       partyFound,
            ProfileFound:     profileFound,
            ProfileStatus:    profileStatus,
            ProfileId:        profileId,
            EntitlementFound: entitlementFound,
            ResourceFound:    resourceFound,
            AllowedChannels:  allowedChannels ?? [],
            EntitlementName:  entitlementName);
    }
}
