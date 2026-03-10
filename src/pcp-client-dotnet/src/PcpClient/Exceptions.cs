namespace PcpClient;

/// <summary>
/// Base exception for all PcpClient errors.
/// </summary>
public class PcpException : Exception
{
    public PcpException() { }
    public PcpException(string message) : base(message) { }
    public PcpException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Endpoint unreachable or context creation failed.
/// </summary>
public class PcpConnectionException : PcpException
{
    public PcpConnectionException(string message) : base(message) { }
    public PcpConnectionException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Server-side context expired. Client should reconnect.
/// </summary>
public class PcpContextExpiredException : PcpException
{
    public PcpContextExpiredException(string message) : base(message) { }
}

/// <summary>
/// Requested metric name does not exist on the endpoint.
/// </summary>
public class PcpMetricNotFoundException : PcpException
{
    public string MetricName { get; }

    public PcpMetricNotFoundException(string metricName)
        : base($"Metric not found: {metricName}")
    {
        MetricName = metricName;
    }
}
