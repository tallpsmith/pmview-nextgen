using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for the PcpClient exception hierarchy.
/// Per contracts/pcpclient-api.md specification.
/// </summary>
public class ExceptionTests
{
    // ── Hierarchy ──

    [Fact]
    public void PcpException_IsException()
    {
        var ex = new PcpException("test error");
        Assert.IsAssignableFrom<Exception>(ex);
    }

    [Fact]
    public void PcpConnectionException_DerivesFromPcpException()
    {
        var ex = new PcpConnectionException("unreachable");
        Assert.IsAssignableFrom<PcpException>(ex);
    }

    [Fact]
    public void PcpContextExpiredException_DerivesFromPcpException()
    {
        var ex = new PcpContextExpiredException("expired");
        Assert.IsAssignableFrom<PcpException>(ex);
    }

    [Fact]
    public void PcpMetricNotFoundException_DerivesFromPcpException()
    {
        var ex = new PcpMetricNotFoundException("no.such.metric");
        Assert.IsAssignableFrom<PcpException>(ex);
    }

    // ── Messages ──

    [Fact]
    public void PcpException_PreservesMessage()
    {
        var ex = new PcpException("something went wrong");
        Assert.Equal("something went wrong", ex.Message);
    }

    [Fact]
    public void PcpConnectionException_PreservesMessage()
    {
        var ex = new PcpConnectionException("connection refused");
        Assert.Equal("connection refused", ex.Message);
    }

    [Fact]
    public void PcpContextExpiredException_PreservesMessage()
    {
        var ex = new PcpContextExpiredException("context 42 expired");
        Assert.Equal("context 42 expired", ex.Message);
    }

    // ── Inner Exceptions ──

    [Fact]
    public void PcpConnectionException_WrapsInnerException()
    {
        var inner = new HttpRequestException("DNS resolution failed");
        var ex = new PcpConnectionException("unreachable", inner);

        Assert.Same(inner, ex.InnerException);
    }

    // ── PcpMetricNotFoundException specifics ──

    [Fact]
    public void PcpMetricNotFoundException_StoresMetricName()
    {
        var ex = new PcpMetricNotFoundException("no.such.metric");
        Assert.Equal("no.such.metric", ex.MetricName);
    }

    [Fact]
    public void PcpMetricNotFoundException_IncludesMetricNameInMessage()
    {
        var ex = new PcpMetricNotFoundException("kernel.bad.name");
        Assert.Contains("kernel.bad.name", ex.Message);
    }

    // ── Default constructors ──

    [Fact]
    public void PcpException_DefaultConstructor()
    {
        var ex = new PcpException();
        Assert.NotNull(ex.Message);
    }
}
