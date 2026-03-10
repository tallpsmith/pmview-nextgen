namespace PcpClient;

/// <summary>
/// A set of instances for a metric. Some metrics have no instance domain.
/// </summary>
public record InstanceDomain(
    string IndomId,
    IReadOnlyList<Instance> Instances);
