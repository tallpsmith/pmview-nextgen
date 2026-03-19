using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Dev-workspace stub for PmviewLogger. Satisfies the ILogger factory calls
/// in addon code during standalone addon builds and tests.
///
/// When the addon is installed into the main pmview-app project, the real
/// PmviewLogger autoload (which owns a live ILoggerFactory) takes precedence
/// and this file is not present.
/// </summary>
namespace PmviewApp;

public partial class PmviewLogger
{
	/// <summary>Get a named ILogger — falls back to NullLogger when no factory is configured.</summary>
	public static ILogger GetLogger(string categoryName) =>
		NullLoggerFactory.Instance.CreateLogger(categoryName);

	/// <summary>Get a typed ILogger — falls back to NullLogger when no factory is configured.</summary>
	public static ILogger<T> GetLogger<T>() => NullLogger<T>.Instance;
}
