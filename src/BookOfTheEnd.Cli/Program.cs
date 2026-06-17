using System.IO;
using System.Runtime.InteropServices;
using BookOfTheEnd.Cli;
using BookOfTheEnd.Models;
using BookOfTheEnd.Services;
using BookOfTheEnd.Services.Scanning;

const string Version = "2.4.6";

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    return 0;
}

PrintBanner();

if (!HasRawAccess())
{
    Console.WriteLine(OperatingSystem.IsWindows()
        ? "  WARNING: not elevated. Re-run from an Administrator terminal for raw disk access.\n"
        : "  WARNING: not running as root. Re-run with: sudo bookoftheend\n");
}

var log = new LoggingService();

var devices = DeviceScanner.Enumerate();
if (devices.Count == 0)
{
    Console.WriteLine("No volumes found. On Linux ensure you run with sudo so /proc/partitions devices are readable.");
    return 1;
}

PrintDevices(devices);

if (args.Contains("--list"))
    return 0;

DriveModel? drive = PromptDevice(devices);
if (drive is null) return 0;

ScanType scanType = PromptScanType(drive);

var engine = CreateEngine(scanType, log);
var options = new ScanOptions { ScanType = scanType };
var controller = new ScanController();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\nCancelling scan...");
    controller.Cancel();
};

const int MaxResults = 50_000;
var results = new List<RecoverableFile>();
var gate = new object();
bool capReached = false;

void OnFound(RecoverableFile f)
{
    lock (gate)
    {
        if (capReached) return;
        results.Add(f);
        if (results.Count >= MaxResults)
        {
            capReached = true;
            controller.Cancel();
        }
    }
}

var progress = new ConsoleProgress();

Console.WriteLine($"\nScanning {drive.Letter} ({drive.FileSystem}, {drive.TotalSizeShort}) — {scanType} scan. Press Ctrl+C to stop.\n");

try
{
    await engine.ScanAsync(drive, options, controller, progress, OnFound);
}
catch (OperationCanceledException)
{
    // Expected on user cancel or result cap.
}
catch (UnauthorizedAccessException ex)
{
    Console.WriteLine($"\n{ex.Message}");
    return 1;
}
catch (Exception ex)
{
    Console.WriteLine($"\nScan failed: {ex.Message}");
    log.Error("CLI scan failed", ex);
    return 1;
}

progress.Finish();
Console.WriteLine();

if (capReached)
    Console.WriteLine($"Reached the {MaxResults:N0}-result limit; narrow the scan to see more.\n");

if (results.Count == 0)
{
    Console.WriteLine("No recoverable files were found.");
    return 0;
}

PrintResults(results);

string? outDir = PromptOutputDir(drive);
if (outDir is null)
{
    Console.WriteLine("Recovery skipped.");
    return 0;
}

var selection = PromptSelection(results);
if (selection.Count == 0)
{
    Console.WriteLine("Nothing selected.");
    return 0;
}

RecoverFiles(selection, outDir, log);
return 0;


// ---------- helpers ----------

static void PrintBanner()
{
    Console.WriteLine();
    Console.WriteLine("  Book of the End — Data Recovery (CLI)  v" + Version);
    Console.WriteLine("  Offline NTFS / FAT32 / signature-carving recovery");
    Console.WriteLine("  ------------------------------------------------------------");
}

static void PrintHelp()
{
    PrintBanner();
    Console.WriteLine(@"
Usage:
  sudo bookoftheend [--list] [--help]

  --list     Enumerate detected volumes and exit.
  --help     Show this help.

Run without arguments for the interactive workflow:
  1. Pick a volume (e.g. /dev/sda1).
  2. Choose Quick (Recycle Bin / MFT / FAT directory) or Deep (signature carving).
  3. Review found files and recover them to an output folder.

Always recover to a DIFFERENT disk than the one you are scanning.");
}

static bool HasRawAccess()
{
    if (OperatingSystem.IsWindows())
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(id);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
    try { return Geteuid() == 0; } catch { return true; }
}

[DllImport("libc", EntryPoint = "geteuid")]
static extern uint Geteuid();

static void PrintDevices(IReadOnlyList<DriveModel> devices)
{
    Console.WriteLine("\n  Detected volumes:\n");
    Console.WriteLine("    #  Device                  File system   Size        Label");
    Console.WriteLine("    -  ----------------------  -----------   ---------   --------------------");
    for (int i = 0; i < devices.Count; i++)
    {
        var d = devices[i];
        Console.WriteLine($"   {i + 1,2}  {Trunc(d.Letter, 22),-22}  {Trunc(d.FileSystem, 11),-11}   {d.TotalSizeShort,-9}   {Trunc(d.VolumeLabel, 20)}");
    }
}

static DriveModel? PromptDevice(IReadOnlyList<DriveModel> devices)
{
    while (true)
    {
        Console.Write("\nSelect a volume number to scan (or 'q' to quit): ");
        string? input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input) || input.Equals("q", StringComparison.OrdinalIgnoreCase))
            return null;
        if (int.TryParse(input, out int n) && n >= 1 && n <= devices.Count)
            return devices[n - 1];
        Console.WriteLine("Invalid selection.");
    }
}

static ScanType PromptScanType(DriveModel drive)
{
    string quickKinds = drive.SupportsMftScan ? "Recycle Bin + NTFS MFT"
        : drive.SupportsFatScan ? "Recycle Bin + FAT32 directories"
        : "Recycle Bin only";
    Console.WriteLine($"\n  Scan type:");
    Console.WriteLine($"    1) Quick  — {quickKinds} (fast, keeps original names)");
    Console.WriteLine($"    2) Deep   — raw signature carving (slow, finds formatted/overwritten files)");
    while (true)
    {
        Console.Write("Choose 1 or 2 [1]: ");
        string? input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input) || input == "1") return ScanType.Quick;
        if (input == "2") return ScanType.Deep;
        Console.WriteLine("Invalid selection.");
    }
}

static IScanEngine CreateEngine(ScanType type, LoggingService log) =>
    type == ScanType.Deep ? new DeepScanEngine(log) : new QuickScanEngine(log);

static void PrintResults(List<RecoverableFile> results)
{
    Console.WriteLine($"  Found {results.Count:N0} recoverable item(s).\n");

    var byCategory = results.GroupBy(r => r.Category)
        .OrderByDescending(g => g.Count());
    foreach (var g in byCategory)
        Console.WriteLine($"    {g.Key,-10} {g.Count(),6:N0}");

    int preview = Math.Min(results.Count, 25);
    Console.WriteLine($"\n  First {preview} item(s):\n");
    Console.WriteLine("    #     Size        Name");
    Console.WriteLine("    ----  ---------   --------------------------------------------");
    for (int i = 0; i < preview; i++)
    {
        var f = results[i];
        Console.WriteLine($"   {i + 1,4}  {f.SizeDisplay,-9}   {Trunc(f.FileName, 44)}");
    }
}

static string? PromptOutputDir(DriveModel drive)
{
    while (true)
    {
        Console.Write("\nOutput folder for recovered files (or 'q' to skip): ");
        string? input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input) || input.Equals("q", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            string full = Path.GetFullPath(input);
            Directory.CreateDirectory(full);
            return full;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cannot use that folder: {ex.Message}");
        }
    }
}

static List<RecoverableFile> PromptSelection(List<RecoverableFile> results)
{
    Console.WriteLine("\nWhat to recover:");
    Console.WriteLine("  - 'all'          : every found file");
    Console.WriteLine("  - '1-20'         : a range by number");
    Console.WriteLine("  - '1,3,5'        : specific numbers");
    Console.Write("Selection [all]: ");
    string? input = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(input) || input.Equals("all", StringComparison.OrdinalIgnoreCase))
        return results;

    var chosen = new List<RecoverableFile>();
    var seen = new HashSet<int>();
    foreach (string token in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (token.Contains('-'))
        {
            var bounds = token.Split('-', 2);
            if (int.TryParse(bounds[0], out int lo) && int.TryParse(bounds[1], out int hi))
                for (int n = lo; n <= hi; n++) AddIndex(n);
        }
        else if (int.TryParse(token, out int n))
        {
            AddIndex(n);
        }
    }
    return chosen;

    void AddIndex(int oneBased)
    {
        int idx = oneBased - 1;
        if (idx >= 0 && idx < results.Count && seen.Add(idx))
            chosen.Add(results[idx]);
    }
}

static void RecoverFiles(List<RecoverableFile> files, string outDir, LoggingService log)
{
    var content = new FileContentService();
    int ok = 0, fail = 0;
    var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    Console.WriteLine($"\nRecovering {files.Count:N0} file(s) to {outDir}\n");

    for (int i = 0; i < files.Count; i++)
    {
        var file = files[i];
        string name = BuildName(file, i, usedNames);
        string dest = Path.Combine(outDir, name);
        try
        {
            using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                content.WriteTo(file, fs, file.Size > 0 ? file.Size : long.MaxValue, CancellationToken.None);
            ok++;
            Console.Write($"\r  [{i + 1}/{files.Count}] recovered {ok}, failed {fail}   ");
        }
        catch (Exception ex)
        {
            fail++;
            log.Warn($"Recover failed for {file.FileName}: {ex.Message}");
            try { File.Delete(dest); } catch { }
        }
    }

    Console.WriteLine($"\n\nDone. {ok:N0} recovered, {fail:N0} failed. Output: {outDir}");
}

static string BuildName(RecoverableFile file, int index, HashSet<string> used)
{
    string baseName = string.IsNullOrWhiteSpace(file.FileName)
        ? $"recovered_{index + 1}{file.Extension}"
        : file.FileName;

    foreach (char c in Path.GetInvalidFileNameChars())
        baseName = baseName.Replace(c, '_');
    if (string.IsNullOrWhiteSpace(baseName)) baseName = $"recovered_{index + 1}";

    string candidate = baseName;
    int suffix = 1;
    while (!used.Add(candidate))
    {
        string stem = Path.GetFileNameWithoutExtension(baseName);
        string ext = Path.GetExtension(baseName);
        candidate = $"{stem}_{suffix++}{ext}";
    }
    return candidate;
}

static string Trunc(string? s, int max)
{
    s ??= "";
    return s.Length <= max ? s : s[..(max - 1)] + "…";
}


/// <summary>Writes a single, self-updating progress line to the console.</summary>
sealed class ConsoleProgress : IProgress<ScanProgress>
{
    private readonly object _gate = new();
    private DateTime _last = DateTime.MinValue;

    public void Report(ScanProgress value)
    {
        lock (_gate)
        {
            // Throttle redraws to keep the terminal readable.
            if ((DateTime.UtcNow - _last).TotalMilliseconds < 100 && value.Percent < 100) return;
            _last = DateTime.UtcNow;
            string line = $"  {value.Percent,5:0.0}%  found {value.FilesFound,6:N0}  {value.ElapsedDisplay}  {Trunc(value.CurrentActivity, 40),-40}";
            Console.Write("\r" + line);
        }
    }

    public void Finish() => Console.Write("\r" + new string(' ', 80) + "\r");

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
