using System.Diagnostics;
using System.Text;

namespace CrashCollector.Console;

/// <summary>
/// Runs WinDbg / cdb.exe headless against a crash dump, executes debugger
/// commands, and captures the full text output.
///
/// Usage:
///   var runner = new WinDbgRunner(symbolPath: symServer.GetSymbolPath());
///   var result = await runner.AnalyseAsync(dumpPath);
///   if (result.Success) Console.WriteLine(result.Output);
///
/// Fails gracefully if cdb.exe is not installed – <see cref="IsAvailable"/>
/// returns false and <see cref="AnalyseAsync"/> returns an error result.
/// </summary>
public sealed class WinDbgRunner
{
    // Default commands: reload symbols, full analysis, call stack, quit
    private static readonly string[] DefaultCommands =
    {
        ".reload /f",
        "!analyze -v",
        "k",
        "q"
    };

    private static readonly Lazy<string?> CachedCdbPath = new(DiscoverCdb);

    private readonly string? _symbolPath;
    private readonly TimeSpan _timeout;

    /// <param name="symbolPath">
    /// Symbol path to set via <c>.sympath</c>.  Use <c>srv*&lt;localDir&gt;</c>
    /// from <see cref="LocalSymbolServer.GetSymbolPath"/>.
    /// If null, cdb.exe uses its default symbol path.
    /// </param>
    /// <param name="timeout">
    /// Maximum wall-clock time for the cdb process. Default: 120 seconds.
    /// </param>
    public WinDbgRunner(string? symbolPath = null, TimeSpan? timeout = null)
    {
        _symbolPath = symbolPath;
        _timeout = timeout ?? TimeSpan.FromSeconds(120);
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>True if cdb.exe was found on this machine.</summary>
    public static bool IsAvailable => CachedCdbPath.Value is not null;

    /// <summary>Full path to the discovered cdb.exe, or null.</summary>
    public static string? CdbExePath => CachedCdbPath.Value;

    /// <summary>
    /// Runs cdb.exe headless against <paramref name="dumpPath"/> with the
    /// default command sequence (<c>.reload /f</c>, <c>!analyze -v</c>,
    /// <c>k</c>) and returns the captured output.
    /// </summary>
    public Task<WinDbgResult> AnalyseAsync(
        string dumpPath,
        CancellationToken ct = default)
        => RunAsync(dumpPath, DefaultCommands, ct);

    /// <summary>
    /// Runs cdb.exe headless with a custom set of commands.
    /// The last command should be <c>q</c> to quit the debugger.
    /// </summary>
    public async Task<WinDbgResult> RunAsync(
        string dumpPath,
        IReadOnlyList<string> commands,
        CancellationToken ct = default)
    {
        var result = new WinDbgResult { DumpPath = dumpPath };

        // ── Pre-flight checks ───────────────────────────────────────────────
        if (!IsAvailable)
        {
            result.Error = "cdb.exe (WinDbg command-line debugger) was not found. " +
                           "Install Debugging Tools for Windows from the Windows SDK, " +
                           "or add cdb.exe to your PATH.";
            return result;
        }

        if (!File.Exists(dumpPath))
        {
            result.Error = $"Dump file does not exist: {dumpPath}";
            return result;
        }

        // ── Build command script ────────────────────────────────────────────
        // Prepend .sympath if configured, then the user commands.
        var script = new List<string>();

        if (!string.IsNullOrWhiteSpace(_symbolPath))
        {
            script.Add($".sympath+ {_symbolPath}");
        }

        script.AddRange(commands);

        // Ensure the script ends with q so cdb terminates
        if (script.Count == 0 || !script[^1].Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
            script.Add("q");

        // Join commands with semicolons for the -c argument
        var cmdLine = string.Join("; ", script);

        // ── Launch cdb.exe ──────────────────────────────────────────────────
        var psi = new ProcessStartInfo
        {
            FileName = CachedCdbPath.Value!,
            // -z <dump>    open dump file
            // -lines       enable source line info
            // -c "cmds"    execute commands then quit
            Arguments = $"-z \"{dumpPath}\" -lines -c \"{cmdLine}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var sw = Stopwatch.StartNew();

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                result.Error = "Failed to start cdb.exe process.";
                return result;
            }

            // Read stdout and stderr concurrently to avoid deadlocks
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);

            // Wait for process exit with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_timeout);

            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout – kill the process
                try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                result.TimedOut = true;
                result.Error = $"cdb.exe timed out after {_timeout.TotalSeconds:F0}s.";
            }

            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            result.ExitCode = proc.HasExited ? proc.ExitCode : -1;

            // Capture whatever output we got (even on timeout)
            try { result.Output = await stdoutTask.ConfigureAwait(false); } catch { /* ignore */ }
            try { result.StdErr = await stderrTask.ConfigureAwait(false); } catch { /* ignore */ }

            result.Success = result.ExitCode == 0 && !result.TimedOut;
            result.CommandsExecuted = script;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            result.Error = "Operation was cancelled.";
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            result.Error = $"{ex.GetType().Name}: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Runs analysis and saves the output to a text file alongside the dump.
    /// Returns the result and the path to the saved report.
    /// </summary>
    public async Task<(WinDbgResult Result, string? ReportPath)> AnalyseAndSaveAsync(
        string dumpPath,
        CancellationToken ct = default)
    {
        var result = await AnalyseAsync(dumpPath, ct).ConfigureAwait(false);

        string? reportPath = null;

        if (result.Output is not null)
        {
            var dir = Path.GetDirectoryName(dumpPath);
            if (dir is not null)
            {
                reportPath = Path.Combine(dir, "windbg-analysis.txt");
                var sb = new StringBuilder();
                sb.AppendLine($"# WinDbg Analysis Report");
                sb.AppendLine($"# Dump:    {dumpPath}");
                sb.AppendLine($"# CDB:     {CachedCdbPath.Value}");
                sb.AppendLine($"# Symbols: {_symbolPath ?? "(default)"}");
                sb.AppendLine($"# Time:    {DateTimeOffset.UtcNow:u}");
                sb.AppendLine($"# Elapsed: {result.ElapsedMs} ms");
                sb.AppendLine($"# Exit:    {result.ExitCode}");
                sb.AppendLine(new string('=', 80));
                sb.AppendLine();
                sb.AppendLine(result.Output);

                if (!string.IsNullOrWhiteSpace(result.StdErr))
                {
                    sb.AppendLine();
                    sb.AppendLine("=== STDERR ===");
                    sb.AppendLine(result.StdErr);
                }

                await File.WriteAllTextAsync(reportPath, sb.ToString(), ct).ConfigureAwait(false);
                reportPath = Path.GetFullPath(reportPath);
            }
        }

        return (result, reportPath);
    }

    // =========================================================================
    //  cdb.exe discovery
    // =========================================================================

    private static string? DiscoverCdb()
    {
        // Well-known Windows SDK install locations
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe",
            @"C:\Program Files\Windows Kits\10\Debuggers\x64\cdb.exe",
            @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x86\cdb.exe",
            @"C:\Program Files\Windows Kits\10\Debuggers\x86\cdb.exe",
            // WinDbg Preview (Store app) installs here
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WindowsApps\cdb.exe"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        // Search PATH via `where`
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "cdb.exe",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (proc.ExitCode == 0)
                {
                    var first = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    if (File.Exists(first)) return first;
                }
            }
        }
        catch { /* not found */ }

        return null;
    }
}

/// <summary>
/// Result of a <see cref="WinDbgRunner"/> execution.
/// </summary>
public sealed class WinDbgResult
{
    /// <summary>Path to the dump that was analysed.</summary>
    public string DumpPath { get; set; } = string.Empty;

    /// <summary>True if cdb.exe ran to completion with exit code 0.</summary>
    public bool Success { get; set; }

    /// <summary>cdb.exe exit code, or -1 if the process didn't exit.</summary>
    public int ExitCode { get; set; } = -1;

    /// <summary>True if the process was killed due to timeout.</summary>
    public bool TimedOut { get; set; }

    /// <summary>Captured standard output (the debugger session text).</summary>
    public string? Output { get; set; }

    /// <summary>Captured standard error.</summary>
    public string? StdErr { get; set; }

    /// <summary>Wall-clock time in milliseconds.</summary>
    public long ElapsedMs { get; set; }

    /// <summary>Non-null when something went wrong.</summary>
    public string? Error { get; set; }

    /// <summary>Commands that were sent to cdb.exe.</summary>
    public IReadOnlyList<string>? CommandsExecuted { get; set; }

    public override string ToString()
    {
        if (Error is not null) return $"FAIL: {Error}";
        var outLen = Output?.Length ?? 0;
        return $"OK ({ElapsedMs}ms, exit={ExitCode}, {outLen} chars output)";
    }
}
