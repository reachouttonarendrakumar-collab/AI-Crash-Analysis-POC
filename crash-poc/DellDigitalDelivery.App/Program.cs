using DellDigitalDelivery.App;

// ═════════════════════════════════════════════════════════════════════════════
//  Dell Digital Delivery Service – Crash Simulator
//  This application simulates a Dell product running on customer systems.
//  It intentionally contains bugs that produce crashes, generating minidump
//  files that feed into the AI-powered crash analysis pipeline.
// ═════════════════════════════════════════════════════════════════════════════

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  Dell Digital Delivery Service – Crash Simulator v3.9.1000  ║");
Console.WriteLine("║  Generating crash dumps for AI analysis pipeline...         ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

var outputDir = Path.GetFullPath(Path.Combine("..", "data", "crash-dumps"));
Console.WriteLine($"[*] Output directory: {outputDir}");
Console.WriteLine();

var generator = new CrashGenerator(outputDir);
var records = generator.GenerateAll();

Console.WriteLine();
Console.WriteLine($"[+] Generated {records.Count} crash dump(s):");
Console.WriteLine();

foreach (var r in records)
{
    Console.WriteLine($"    {r.CrashId}  {r.ExceptionCode}  {r.ExceptionName,-25}  {r.ScenarioName}");
}

Console.WriteLine();
Console.WriteLine($"[+] Crash dumps written to: {outputDir}");
Console.WriteLine($"[+] PDB files available at: {Path.GetFullPath(Path.Combine(AppContext.BaseDirectory))}");
Console.WriteLine("[+] Done. These dumps will be consumed by the crash analysis pipeline.");
