using CustomerEntitlement.Api.Domain;

namespace CustomerEntitlement.Tests;

/// <summary>
/// Exercises every domain record constructor, property access, and equality
/// so coverage tools count them as covered.
/// </summary>
public class DomainModelTests
{
    [Fact]
    public void Party_Properties_RoundTrip()
    {
        var party = new Party("P-001", "Person", "Kiran Jivan");

        Assert.Equal("P-001", party.Id);
        Assert.Equal("Person", party.Type);
        Assert.Equal("Kiran Jivan", party.Name);
        Assert.Equal(party, new Party("P-001", "Person", "Kiran Jivan"));
    }

    [Fact]
    public void CustomerAccessProfile_Properties_RoundTrip()
    {
        var profile = new CustomerAccessProfile("PROF-001", "Active");

        Assert.Equal("PROF-001", profile.Id);
        Assert.Equal("Active", profile.Status);
        Assert.Equal(profile, new CustomerAccessProfile("PROF-001", "Active"));
    }

    [Fact]
    public void Entitlement_Properties_RoundTrip()
    {
        var ent = new Entitlement("ENT-01", "CallCenterServicing", "ServiceRequest");

        Assert.Equal("ENT-01", ent.Id);
        Assert.Equal("CallCenterServicing", ent.Name);
        Assert.Equal("ServiceRequest", ent.Action);
        Assert.Equal(ent, new Entitlement("ENT-01", "CallCenterServicing", "ServiceRequest"));
    }

    [Fact]
    public void UtilizationLimit_Properties_RoundTrip()
    {
        var limit = new UtilizationLimit("LIM-01", "Amount", 500m, "24h", "NZD");

        Assert.Equal("LIM-01", limit.Id);
        Assert.Equal("Amount", limit.Metric);
        Assert.Equal(500m, limit.Value);
        Assert.Equal("24h", limit.Period);
        Assert.Equal("NZD", limit.Currency);
        Assert.Equal(limit, new UtilizationLimit("LIM-01", "Amount", 500m, "24h", "NZD"));
    }

    [Fact]
    public void Channel_Properties_RoundTrip()
    {
        var channel = new Channel("CH-CALL", "Call Center");

        Assert.Equal("CH-CALL", channel.Id);
        Assert.Equal("Call Center", channel.Name);
        Assert.Equal(channel, new Channel("CH-CALL", "Call Center"));
    }

    [Fact]
    public void Service_Properties_RoundTrip()
    {
        var service = new Service("SRV-STOP", "Stop Payment");

        Assert.Equal("SRV-STOP", service.Id);
        Assert.Equal("Stop Payment", service.Name);
        Assert.Equal(service, new Service("SRV-STOP", "Stop Payment"));
    }

    [Fact]
    public void ProductInstance_Properties_RoundTrip()
    {
        var product = new ProductInstance("ACC-1001", "Checking");

        Assert.Equal("ACC-1001", product.Id);
        Assert.Equal("Checking", product.Type);
        Assert.Equal(product, new ProductInstance("ACC-1001", "Checking"));
    }

    [Fact]
    public void EvaluationMetadata_Properties_RoundTrip()
    {
        var meta = new EvaluationMetadata("PROF-001", true);

        Assert.Equal("PROF-001", meta.ProfileId);
        Assert.True(meta.ChannelValidated);
        Assert.Equal(meta, new EvaluationMetadata("PROF-001", true));
    }
}
