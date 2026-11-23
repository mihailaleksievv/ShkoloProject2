// Owner: Participant 3 – Data Developer
using Shkolo.Application.Abstractions;
using Shkolo.Domain;

namespace Shkolo.Infrastructure.Persistence;

/// <summary>
/// Creates the initial database. Called by <see cref="JsonDataStore"/> only when no data file exists,
/// so demo data is generated exactly once.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly IClock _clock;
    private readonly IPasswordHasher _hasher;

    public DatabaseInitializer(IClock clock, IPasswordHasher hasher)
    {
        _clock = clock;
        _hasher = hasher;
    }

    public SchoolDatabase CreateInitialDatabase() => new DemoDataGenerator(_clock, _hasher).Generate();
}
