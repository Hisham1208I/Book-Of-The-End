using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services;

/// <summary>
/// Persists scan results to %AppData%\Book of the End\last-session.json so the user
/// can reopen a long scan without re-running it.
/// </summary>
public sealed class SessionService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly LoggingService _log;

    public SessionService(LoggingService log) => _log = log;

    private static string SessionPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Book of the End", "last-session.json");

    public bool HasSession => File.Exists(SessionPath);

    public SessionData? Load()
    {
        if (!File.Exists(SessionPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<SessionData>(File.ReadAllText(SessionPath), JsonOpts);
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to load session: {ex.Message}");
            return null;
        }
    }

    public void Save(string driveLetter, ScanType scanType, IEnumerable<RecoverableFile> files)
    {
        try
        {
            var list = files.Select(ToDto).ToList();
            var session = new SessionData
            {
                SavedAt = DateTime.Now,
                DriveLetter = driveLetter,
                ScanType = scanType.ToString(),
                TotalFound = list.Count,
                Files = list
            };
            Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
            File.WriteAllText(SessionPath, JsonSerializer.Serialize(session, JsonOpts));
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to save session: {ex.Message}");
        }
    }

    public void Delete()
    {
        try { if (File.Exists(SessionPath)) File.Delete(SessionPath); }
        catch { }
    }

    public IReadOnlyList<RecoverableFile> ToFiles(SessionData session) =>
        session.Files.Select(FromDto).ToList();

    private static SessionFile ToDto(RecoverableFile f) => new()
    {
        Source = f.Source.ToString(),
        DriveLetter = f.DriveLetter,
        FileName = f.FileName,
        IsNameSynthesized = f.IsNameSynthesized,
        Extension = f.Extension,
        Category = f.Category.ToString(),
        Size = f.Size,
        Modified = f.Modified,
        Created = f.Created,
        Deleted = f.Deleted,
        OriginalPath = f.OriginalPath,
        Status = f.Status.ToString(),
        Quality = f.Quality.ToString(),
        RawDataPath = f.RawDataPath,
        ResidentDataBase64 = f.ResidentData is { Length: > 0 } d ? Convert.ToBase64String(d) : null,
        DataRuns = f.DataRuns?.Select(r => new SessionDataRun
        {
            StartCluster = r.StartCluster,
            ClusterCount = r.ClusterCount
        }).ToList(),
        BytesPerCluster = f.BytesPerCluster,
        CarveOffset = f.CarveOffset,
        SignatureTag = f.SignatureTag,
        FatStartCluster = f.FatStartCluster,
        FatClusterChain = f.FatClusterChain?.ToList(),
        FatDataStartOffset = f.FatDataStartOffset
    };

    private static RecoverableFile FromDto(SessionFile d)
    {
        Enum.TryParse<RecoverySource>(d.Source, out var source);
        Enum.TryParse<FileCategory>(d.Category, out var category);
        Enum.TryParse<RecoveryStatus>(d.Status, out var status);
        Enum.TryParse<RecoveryQuality>(d.Quality, out var quality);

        return new RecoverableFile
        {
            Source = source,
            DriveLetter = d.DriveLetter,
            FileName = d.FileName,
            IsNameSynthesized = d.IsNameSynthesized,
            Extension = d.Extension,
            Category = category,
            Size = d.Size,
            Modified = d.Modified,
            Created = d.Created,
            Deleted = d.Deleted,
            OriginalPath = d.OriginalPath,
            Status = status,
            Quality = quality,
            RawDataPath = d.RawDataPath,
            ResidentData = d.ResidentDataBase64 is not null
                ? Convert.FromBase64String(d.ResidentDataBase64)
                : null,
            DataRuns = d.DataRuns?
                .Select(r => new DataRun(r.StartCluster, r.ClusterCount))
                .ToList(),
            BytesPerCluster = d.BytesPerCluster,
            CarveOffset = d.CarveOffset,
            SignatureTag = d.SignatureTag,
            FatStartCluster = d.FatStartCluster,
            FatClusterChain = d.FatClusterChain,
            FatDataStartOffset = d.FatDataStartOffset
        };
    }
}
