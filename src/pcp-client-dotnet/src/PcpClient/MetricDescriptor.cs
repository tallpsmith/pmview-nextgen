namespace PcpClient;

/// <summary>
/// Metadata about a single PCP metric. Immutable once fetched.
/// </summary>
public record MetricDescriptor(
    string Name,
    string Pmid,
    MetricType Type,
    MetricSemantics Semantics,
    string? Units,
    string? IndomId,
    string OneLineHelp,
    string? LongHelp);

/// <summary>
/// A single instance within an instance domain (e.g., one CPU, one disk).
/// </summary>
public record Instance(int Id, string Name);

/// <summary>
/// PCP metric data type.
/// </summary>
public enum MetricType
{
    Float,
    Double,
    U32,
    U64,
    I32,
    I64,
    String
}

/// <summary>
/// How to interpret metric values.
/// </summary>
public enum MetricSemantics
{
    Instant,
    Counter,
    Discrete
}

/// <summary>
/// Connection lifecycle states.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}
