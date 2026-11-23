// Owner: Participant 3 – Data Developer
using System.Globalization;
using Shkolo.Application.Abstractions;

namespace Shkolo.Infrastructure.Persistence;

/// <summary>Архиви на базата в папка Backups – пазят се последните 5.</summary>
public sealed class BackupManager : IBackupProvider
{
    public const string MainFileName = "school-data.json";
    private const string Prefix = "school-data_";
    private const string TimestampFormat = "yyyyMMdd_HHmmss_fff";

    private readonly string _dataDirectory;
    private readonly IClock _clock;
    private readonly int _maxBackups;

    public BackupManager(string dataDirectory, IClock clock, int maxBackups = 5)
    {
        _dataDirectory = dataDirectory;
        _clock = clock;
        _maxBackups = maxBackups;
    }

    public string BackupDirectory => Path.Combine(_dataDirectory, "Backups");

    private string MainFilePath => Path.Combine(_dataDirectory, MainFileName);

    /// <summary>Copies <paramref name="sourceFile"/> into the backup folder and deletes the oldest extra backups.</summary>
    public void CreateBackup(string sourceFile)
    {
        Directory.CreateDirectory(BackupDirectory);
        File.Copy(sourceFile, NextBackupPath(), overwrite: false);
        Rotate();
    }

    /// <summary>
    /// Backup before an ordinary save, at most once per <paramref name="minInterval"/>. Without this, every small
    /// save (even a failed-login audit) would push real recovery points out of the five-file window within seconds.
    /// </summary>
    public void CreateBackupIfDue(string sourceFile, TimeSpan minInterval)
    {
        var newest = GetBackups().FirstOrDefault();
        var now = DateTime.SpecifyKind(_clock.LocalNow, DateTimeKind.Local).ToUniversalTime();
        if (newest is null || now - newest.CreatedAtUtc >= minInterval)
        {
            CreateBackup(sourceFile);
        }
    }

    public IReadOnlyList<BackupInfo> GetBackups()
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return [];
        }

        return Directory.GetFiles(BackupDirectory, Prefix + "*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new BackupInfo(f.Name, ParseTimestamp(f.Name) ?? f.LastWriteTimeUtc, f.Length))
            .ToList();
    }

    public void RestoreBackup(string backupFileName)
    {
        var known = GetBackups().Any(b => b.FileName == backupFileName);
        if (!known || backupFileName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new DataStoreException($"Архивът „{backupFileName}“ не съществува.");
        }

        try
        {
            // Read first: backing up the current file may rotate out the very backup being restored.
            var content = File.ReadAllBytes(Path.Combine(BackupDirectory, backupFileName));
            if (File.Exists(MainFilePath))
            {
                CreateBackup(MainFilePath);
            }

            File.WriteAllBytes(MainFilePath, content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DataStoreException("Архивът не може да бъде възстановен: " + ex.Message, ex);
        }
    }

    private string NextBackupPath()
    {
        var stamp = _clock.LocalNow.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var path = Path.Combine(BackupDirectory, $"{Prefix}{stamp}.json");
        for (var n = 1; File.Exists(path); n++)
        {
            path = Path.Combine(BackupDirectory, $"{Prefix}{stamp}-{n}.json");
        }

        return path;
    }

    private void Rotate()
    {
        foreach (var old in GetBackups().Skip(_maxBackups))
        {
            File.Delete(Path.Combine(BackupDirectory, old.FileName));
        }
    }

    private static DateTime? ParseTimestamp(string fileName)
    {
        var stamp = fileName[Prefix.Length..].Split('-', '.')[0];
        return DateTime.TryParseExact(stamp, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local)
            ? local.ToUniversalTime()
            : null;
    }
}
