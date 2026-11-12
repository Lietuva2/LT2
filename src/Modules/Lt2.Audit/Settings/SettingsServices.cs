using System.Text.Json;
using Lt2.Audit.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;

namespace Lt2.Audit.Settings;

/// <summary>Reads settings with a short in-memory cache; a missing row means the design default.</summary>
internal sealed class SettingsReader(AuditDb db, IMemoryCache cache) : ISettings
{
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    internal static string CacheKey(string name) => "setting:" + name;

    public async Task<T> GetAsync<T>(SettingKey<T> key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var raw = await cache.GetOrCreateAsync(CacheKey(key.Name), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await db.Settings.AsNoTracking()
                .Where(s => s.Key == key.Name)
                .Select(s => s.Value)
                .FirstOrDefaultAsync(cancellationToken);
        });

        if (raw is null)
        {
            return key.Default;
        }

        using var document = JsonDocument.Parse(raw);
        return key.TryRead(document.RootElement, out var value) ? value : key.Default;
    }
}

internal sealed class SettingsAdministration(AuditDb db, IAuditLog audit, IMemoryCache cache, TimeProvider clock)
    : ISettingsAdministration
{
    public async Task<IReadOnlyList<SettingValue>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, cancellationToken);
        return SettingKeys.All
            .Select(k => rows.TryGetValue(k.Name, out var row)
                ? new SettingValue(k.Name, Parse(row.Value), row.ChangedBy, row.ChangedAt)
                : new SettingValue(k.Name, k.DefaultValue, null, null))
            .ToList();
    }

    public async Task<Result<SettingValue>> UpdateAsync(
        string key,
        JsonElement value,
        Guid actorUserId,
        string actorRole,
        string? ipHash,
        CancellationToken cancellationToken)
    {
        var definition = SettingKeys.Find(key);
        if (definition is null)
        {
            return Error.NotFound("setting.unknown");
        }

        if (!definition.IsValid(value))
        {
            return Error.Validation("setting.invalid", "value");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == definition.Name, cancellationToken);
        var previous = row?.Value ?? definition.DefaultValue.GetRawText();
        if (row is null)
        {
            row = new Setting { Key = definition.Name };
            db.Settings.Add(row);
        }

        row.Value = value.GetRawText();
        row.ChangedBy = actorUserId;
        row.ChangedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        var details = JsonSerializer.SerializeToElement(
            new { from = Parse(previous), to = value },
            JsonSerializerOptions.Web);
        await audit.WriteAsync(
            new AuditEntry("setting.changed", actorUserId, actorRole, definition.Name, details, ipHash),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        cache.Remove(SettingsReader.CacheKey(definition.Name));
        return new SettingValue(definition.Name, value, row.ChangedBy, row.ChangedAt);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
