namespace CustomerEntitlement.Api.Domain;

public record UtilizationLimit(string Id, string Metric, decimal Value, string Period, string Currency);
