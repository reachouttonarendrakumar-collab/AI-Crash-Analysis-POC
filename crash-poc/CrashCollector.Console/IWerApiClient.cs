using CrashCollector.Console.Models;

namespace CrashCollector.Console;

/// <summary>
/// Abstracts access to Microsoft Windows Error Reporting (WER) REST API.
/// </summary>
public interface IWerApiClient
{
    /// <summary>
    /// Lists crash reports for the given executable name.
    /// </summary>
    /// <param name="executableName">e.g. DELL.DIGITAL.DELIVERY.SERVICE.SUBAGENT.EXE</param>
    /// <param name="maxResults">Maximum number of reports to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<CrashReport>> GetCrashesAsync(
        string executableName,
        int maxResults = 50,
        CancellationToken ct = default);
}
