namespace Lt2.SharedKernel;

/// <summary>What the migrator needs to know about one module's database schema.</summary>
public interface IModuleDatabase
{
    /// <summary>Module name, for logs.</summary>
    string Module { get; }

    /// <summary>PostgreSQL schemas the module owns.</summary>
    IReadOnlyList<string> Schemas { get; }

    /// <summary>Applies the module's EF Core migrations using the given connection string.</summary>
    Task MigrateAsync(string connectionString, CancellationToken cancellationToken);
}
