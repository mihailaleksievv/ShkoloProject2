// Owner: Participant 3 – Data Developer
using System.Globalization;
using System.Text.Json;
using Shkolo.Application.Abstractions;
using Shkolo.Domain;

namespace Shkolo.Infrastructure.Persistence;

/// <summary>Чете и записва цялата база в school-data.json. Записът е безопасен: първо във временен файл, после замяна, и се прави архив.</summary>
public sealed class JsonDataStore : IDataStore
{
    /// <summary>Minimum time between automatic backups made before ordinary saves.</summary>
    public static readonly TimeSpan BackupInterval = TimeSpan.FromMinutes(10);

    private readonly string _dataDirectory;
    private readonly BackupManager _backups;
    private readonly Func<SchoolDatabase> _seedFactory;
    private readonly IClock _clock;
    private readonly IntegrityValidator? _validator;

    public JsonDataStore(string dataDirectory, BackupManager backups, Func<SchoolDatabase> seedFactory, IClock clock, IntegrityValidator? validator = null)
    {
        _dataDirectory = dataDirectory;
        _backups = backups;
        _seedFactory = seedFactory;
        _clock = clock;
        _validator = validator;
    }

    public string DataFilePath => Path.Combine(_dataDirectory, BackupManager.MainFileName);

    private string TempFilePath => DataFilePath + ".tmp";

    public LoadResult Load()
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            if (!File.Exists(DataFilePath))
            {
                var seeded = _seedFactory();
                Save(seeded);
                return new LoadResult(LoadStatus.CreatedNew, seeded, "Създадена е нова база с демонстрационни данни.", []);
            }

            var json = File.ReadAllText(DataFilePath);
            var database = TryDeserialize(json);
            if (database is null)
            {
                var copy = PreserveCorruptedFile();
                return new LoadResult(LoadStatus.Corrupted, null,
                    $"Файлът с данни е повреден и не може да бъде прочетен. Копие е запазено като „{copy}“.", []);
            }

            if (database.SchemaVersion > SchoolDatabase.CurrentSchemaVersion)
            {
                return new LoadResult(LoadStatus.UnsupportedVersion, null,
                    $"Файлът е създаден от по-нова версия (схема {database.SchemaVersion}). Обновете приложението.", []);
            }

            var issues = _validator?.Validate(database) ?? [];
            return issues.Count > 0
                ? new LoadResult(LoadStatus.IntegrityProblems, database,
                    $"Открити са {issues.Count} проблема в целостта на данните. Приложението работи в режим само за четене.", issues)
                : new LoadResult(LoadStatus.Loaded, database, null, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DataStoreException("Файлът с данни не може да бъде прочетен: " + ex.Message, ex);
        }
    }

    public void Save(SchoolDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (database.Users.Count == 0)
        {
            // Guard against accidentally replacing real data with an empty database.
            throw new DataStoreException("Отказан запис: базата не съдържа потребители.");
        }

        try
        {
            Directory.CreateDirectory(_dataDirectory);
            database.LastSavedAtUtc = _clock.UtcNow;
            File.WriteAllText(TempFilePath, JsonSerializer.Serialize(database, JsonOptions.Default));

            if (TryDeserialize(File.ReadAllText(TempFilePath)) is null)
            {
                throw new DataStoreException("Записаният файл не може да бъде прочетен обратно.");
            }

            if (File.Exists(DataFilePath))
            {
                _backups.CreateBackupIfDue(DataFilePath, BackupInterval);
                File.Replace(TempFilePath, DataFilePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(TempFilePath, DataFilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DataStoreException)
        {
            DeleteTempFile();
            throw ex as DataStoreException ?? new DataStoreException("Данните не могат да бъдат записани: " + ex.Message, ex);
        }
    }

    private static SchoolDatabase? TryDeserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SchoolDatabase>(json, JsonOptions.Default);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string PreserveCorruptedFile()
    {
        var stamp = _clock.LocalNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var name = $"school-data.corrupt-{stamp}.json";
        File.Copy(DataFilePath, Path.Combine(_dataDirectory, name), overwrite: true);
        return name;
    }

    private void DeleteTempFile()
    {
        try
        {
            File.Delete(TempFilePath);
        }
        catch (IOException)
        {
            // The temp file is harmless; the next save overwrites it.
        }
    }
}
