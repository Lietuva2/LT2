using System.Text.Json;

namespace Lt2.SharedKernel;

/// <summary>A runtime setting (design 5.6): stored in <c>config.setting</c>, changed by an administrator without a deploy.</summary>
public interface ISettingKey
{
    string Name { get; }

    JsonElement DefaultValue { get; }

    /// <summary>Whether <paramref name="value"/> has the right type and range for this setting.</summary>
    bool IsValid(JsonElement value);
}

public sealed class SettingKey<T> : ISettingKey
{
    private readonly Func<T, bool> _isValid;

    public SettingKey(string name, T defaultValue, Func<T, bool>? isValid = null)
    {
        Name = name;
        Default = defaultValue;
        _isValid = isValid ?? (_ => true);
    }

    public string Name { get; }

    public T Default { get; }

    public JsonElement DefaultValue => JsonSerializer.SerializeToElement(Default, JsonSerializerOptions.Web);

    public bool IsValid(JsonElement value)
    {
        if (!TryRead(value, out var typed))
        {
            return false;
        }

        return _isValid(typed);
    }

    public bool TryRead(JsonElement value, out T result)
    {
        result = Default;
        try
        {
            var read = value.Deserialize<T>(JsonSerializerOptions.Web);
            if (read is null)
            {
                return false;
            }

            result = read;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>Every runtime setting the application knows, with the defaults from the design.</summary>
public static class SettingKeys
{
    /// <summary>Whether registration needs an invitation code (closed trial, design 6.2).</summary>
    public static readonly SettingKey<bool> RegistrationRequiresInvitation = new("Registration:RequireInvitation", true);

    /// <summary>Days a pending account (address never proven, or registration never completed) is kept (design 6.2).</summary>
    public static readonly SettingKey<int> PendingAccountDays = new("Registration:PendingAccountDays", 7, v => v is > 0 and <= 90);

    /// <summary>Default appeal threshold offered to the initiator (design 8.4).</summary>
    public static readonly SettingKey<int> DigestThresholdDefault = new("Digest:ThresholdDefault", 100, v => v > 0);

    /// <summary>Most summaries an MP receives in a week (design 8.4).</summary>
    public static readonly SettingKey<int> DigestMaxPerWeek = new("Digest:MaxPerWeek", 2, v => v is > 0 and <= 7);

    /// <summary>Surge note: share of recent positions from new accounts (design 6.6).</summary>
    public static readonly SettingKey<decimal> SurgeMinimumShare = new("Surge:MinimumShare", 0.30m, v => v is > 0 and <= 1);

    /// <summary>Surge note: minimum number of new accounts (design 6.6).</summary>
    public static readonly SettingKey<int> SurgeMinimumAccounts = new("Surge:MinimumAccounts", 50, v => v > 0);

    /// <summary>Surge note: window in hours (design 6.6).</summary>
    public static readonly SettingKey<int> SurgeWindowHours = new("Surge:WindowHours", 72, v => v is > 0 and <= 24 * 30);

    public static readonly IReadOnlyList<ISettingKey> All =
    [
        RegistrationRequiresInvitation,
        PendingAccountDays,
        DigestThresholdDefault,
        DigestMaxPerWeek,
        SurgeMinimumShare,
        SurgeMinimumAccounts,
        SurgeWindowHours,
    ];

    public static ISettingKey? Find(string name) =>
        All.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Reads runtime settings. Implemented by the Audit module, which owns <c>config.setting</c>.</summary>
public interface ISettings
{
    Task<T> GetAsync<T>(SettingKey<T> key, CancellationToken cancellationToken = default);
}
