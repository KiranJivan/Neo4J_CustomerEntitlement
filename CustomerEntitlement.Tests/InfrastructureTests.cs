using CustomerEntitlement.Api.Application;
using CustomerEntitlement.Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Neo4j.Driver;

namespace CustomerEntitlement.Tests;

// Shared Neo4j mock helpers
file static class Neo4jMocks
{
    // Builds an IRecord mock with all Cypher projection fields populated.
    internal static Mock<IRecord> Record(
        bool partyFound        = true,
        bool profileFound      = true,
        string? profileStatus  = "Active",
        string? profileId      = "PROF-001",
        bool entitlementFound  = true,
        bool resourceFound     = true,
        List<string>? channels = null,
        string? entName        = "CallCenterServicing")
    {
        var r = new Mock<IRecord>();
        r.Setup(x => x["partyFound"]).Returns((object)partyFound);
        r.Setup(x => x["profileFound"]).Returns((object)profileFound);
        r.Setup(x => x["profileStatus"]).Returns(profileStatus == null ? null! : (object)profileStatus);
        r.Setup(x => x["profileId"]).Returns(profileId == null ? null! : (object)profileId);
        r.Setup(x => x["entitlementFound"]).Returns((object)entitlementFound);
        r.Setup(x => x["resourceFound"]).Returns((object)resourceFound);
        r.Setup(x => x["allowedChannels"]).Returns((object)(channels ?? []));
        r.Setup(x => x["entitlementName"]).Returns(entName == null ? null! : (object)entName);
        return r;
    }

    // Returns a session that delivers record directly from ExecuteReadAsync<IRecord> — no cursor chain needed.
    internal static Mock<IAsyncSession> SessionReturningRecord(IRecord record)
    {
        var session = new Mock<IAsyncSession>();
        session
            .Setup(s => s.ExecuteReadAsync(
                It.IsAny<Func<IAsyncQueryRunner, Task<IRecord>>>(),
                It.IsAny<Action<TransactionConfigBuilder>?>()))
            .ReturnsAsync(record);
        session.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return session;
    }

    // Returns a session that delivers nodeCount from ExecuteReadAsync<long> (used by the seed service).
    internal static Mock<IAsyncSession> SessionReturningCount(
        long nodeCount,
        Mock<IAsyncQueryRunner>? writeRunner = null)
    {
        var session = new Mock<IAsyncSession>();

        session
            .Setup(s => s.ExecuteReadAsync(
                It.IsAny<Func<IAsyncQueryRunner, Task<long>>>(),
                It.IsAny<Action<TransactionConfigBuilder>?>()))
            .ReturnsAsync(nodeCount);

        if (writeRunner is not null)
        {
            session
                .Setup(s => s.ExecuteWriteAsync(
                    It.IsAny<Func<IAsyncQueryRunner, Task>>(),
                    It.IsAny<Action<TransactionConfigBuilder>?>()))
                .Returns<Func<IAsyncQueryRunner, Task>, Action<TransactionConfigBuilder>?>(
                    (work, _) => work(writeRunner.Object));
        }

        session.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return session;
    }

    internal static IConfiguration Config(string database = "neo4j") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Neo4j:Database"] = database })
            .Build();
}

// Repository tests
public class Neo4jEntitlementRepositoryTests
{
    [Fact]
    public async Task EvaluateAsync_GrantedPath_ReturnsAllow()
    {
        var record  = Neo4jMocks.Record(channels: ["CH-CALL"]);
        var session = Neo4jMocks.SessionReturningRecord(record.Object);
        var driver  = new Mock<IDriver>();
        driver.Setup(d => d.AsyncSession(It.IsAny<Action<SessionConfigBuilder>>()))
              .Returns(session.Object);

        var repo   = new Neo4jEntitlementRepository(driver.Object, Neo4jMocks.Config());
        var result = await repo.EvaluateAsync(
            new EntitlementQuery("P-001", "ServiceRequest", "SRV-STOP", "CH-CALL"));

        Assert.True(result.IsAllowed);
        Assert.Equal("CallCenterServicing", result.GrantedTerm);
        Assert.Equal("PROF-001", result.Details.ProfileId);
    }

    [Fact]
    public async Task EvaluateAsync_SubjectNotFound_ReturnsDeny()
    {
        var record  = Neo4jMocks.Record(
            partyFound: false, profileFound: false, profileStatus: null,
            profileId: null, entitlementFound: false, resourceFound: false, entName: null);
        var session = Neo4jMocks.SessionReturningRecord(record.Object);
        var driver  = new Mock<IDriver>();
        driver.Setup(d => d.AsyncSession(It.IsAny<Action<SessionConfigBuilder>>()))
              .Returns(session.Object);

        var repo   = new Neo4jEntitlementRepository(driver.Object, Neo4jMocks.Config());
        var result = await repo.EvaluateAsync(
            new EntitlementQuery("P-GHOST", "ServiceRequest", "SRV-STOP"));

        Assert.False(result.IsAllowed);
        Assert.Contains("not found", result.Reason);
    }

    [Fact]
    public void MapToRow_MapsAllFieldsCorrectly()
    {
        var record = Neo4jMocks.Record(
            partyFound: true, profileFound: true, profileStatus: "Active",
            profileId: "PROF-001", entitlementFound: true, resourceFound: true,
            channels: ["CH-CALL", "CH-ATM"], entName: "SomeEntitlement");

        var row = Neo4jEntitlementRepository.MapToRow(record.Object);

        Assert.True(row.PartyFound);
        Assert.True(row.ProfileFound);
        Assert.Equal("Active", row.ProfileStatus);
        Assert.Equal("PROF-001", row.ProfileId);
        Assert.True(row.EntitlementFound);
        Assert.True(row.ResourceFound);
        Assert.Equal(2, row.AllowedChannels.Count);
        Assert.Equal("SomeEntitlement", row.EntitlementName);
    }

    [Fact]
    public void MapToRow_NullableFields_AreMappedAsNull()
    {
        var record = Neo4jMocks.Record(
            partyFound: false, profileFound: false, profileStatus: null,
            profileId: null, entitlementFound: false, resourceFound: false,
            channels: null, entName: null);

        var row = Neo4jEntitlementRepository.MapToRow(record.Object);

        Assert.Null(row.ProfileStatus);
        Assert.Null(row.ProfileId);
        Assert.Null(row.EntitlementName);
        Assert.Empty(row.AllowedChannels);
    }
}

// Seed hosted service tests
public class Neo4jSeedHostedServiceTests
{
    private static Neo4jSeedHostedService CreateService(IDriver driver)
    {
        var logger = new Mock<ILogger<Neo4jSeedHostedService>>().Object;
        return new Neo4jSeedHostedService(driver, logger, Neo4jMocks.Config());
    }

    [Fact]
    public async Task StartAsync_DatabaseHasData_SkipsSeed()
    {
        var session = Neo4jMocks.SessionReturningCount(nodeCount: 10);
        var driver  = new Mock<IDriver>();
        driver.Setup(d => d.AsyncSession(It.IsAny<Action<SessionConfigBuilder>>()))
              .Returns(session.Object);

        await CreateService(driver.Object).StartAsync(CancellationToken.None);

        session.Verify(
            s => s.ExecuteWriteAsync(
                It.IsAny<Func<IAsyncQueryRunner, Task>>(),
                It.IsAny<Action<TransactionConfigBuilder>?>()),
            Times.Never);
    }

    [Fact]
    public async Task StartAsync_EmptyDatabase_RunsSeed()
    {
        var writeCursor = new Mock<IResultCursor>();
        var writeRunner = new Mock<IAsyncQueryRunner>();
        writeRunner.Setup(r => r.RunAsync(It.IsAny<string>())).ReturnsAsync(writeCursor.Object);

        var session = Neo4jMocks.SessionReturningCount(nodeCount: 0, writeRunner: writeRunner);
        var driver  = new Mock<IDriver>();
        driver.Setup(d => d.AsyncSession(It.IsAny<Action<SessionConfigBuilder>>()))
              .Returns(session.Object);

        await CreateService(driver.Object).StartAsync(CancellationToken.None);

        session.Verify(
            s => s.ExecuteWriteAsync(
                It.IsAny<Func<IAsyncQueryRunner, Task>>(),
                It.IsAny<Action<TransactionConfigBuilder>?>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_DriverThrows_DoesNotPropagateException()
    {
        var driver = new Mock<IDriver>();
        driver.Setup(d => d.AsyncSession(It.IsAny<Action<SessionConfigBuilder>>()))
              .Throws(new InvalidOperationException("DB unreachable"));

        var ex = await Record.ExceptionAsync(
            () => CreateService(driver.Object).StartAsync(CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task StopAsync_CompletesWithoutError()
    {
        var driver = new Mock<IDriver>();
        var ex     = await Record.ExceptionAsync(
            () => CreateService(driver.Object).StopAsync(CancellationToken.None));

        Assert.Null(ex);
    }
}
