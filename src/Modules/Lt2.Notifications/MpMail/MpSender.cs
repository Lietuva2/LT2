using Lt2.Notifications.Digests;
using Lt2.Notifications.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Notifications.MpMail;

/// <summary>What one MP's delivery will be when a summary is let go (design 8.19.3).</summary>
internal sealed record PlannedDelivery(MpRecipient Mp, string Official, string? Extra, string? Address, MpEmailPref? Pref);

/// <summary>
/// What every sending of MP mail shares (design 8.19.3 to 8.19.6), inside the caller's transaction: who has had the
/// introduction letter, the MPs' choices, the suppression list (by keyed hash), the preferences links (derived, stored hashed,
/// made when the first email to an MP is queued) and the outbox rows. Addresses come only from
/// <see cref="IMpMailAddresses"/> and go only into outbox rows.
/// </summary>
internal sealed class MpSender(IMpMailAddresses addresses, IOutbox outbox, MailKeys keys, TimeProvider clock)
{
    private readonly Dictionary<(long, string), string> _tokens = [];

    /// <summary>The official addresses of the given MPs, for sending only.</summary>
    public Task<IReadOnlyDictionary<long, string>> AddressesAsync(IEnumerable<long> seimasPersonIds, CancellationToken ct) =>
        addresses.OfficialAddressesForSendingAsync(seimasPersonIds.Distinct().ToList(), ct);

    /// <summary>The MPs who have had the introduction letter (design 8.19.5).</summary>
    public static async Task<HashSet<long>> IntroducedAsync(NotificationsDb db, CancellationToken ct)
    {
        var rows = await (from d in db.DigestDeliveries
                          join r in db.DigestRuns on d.RunId equals r.Id
                          where d.Recipient == Recipients.Official
                                && (d.Status == DeliveryStatuses.Introduced
                                    || (r.Kind == SendingRules.Introduction && (d.Status == DeliveryStatuses.Queued || d.Status == DeliveryStatuses.Sent)))
                          select d.SeimasPersonId).Distinct().ToListAsync(ct);
        return rows.ToHashSet();
    }

    public static async Task<Dictionary<long, MpEmailPref>> PrefsAsync(NotificationsDb db, IReadOnlyCollection<long> seimasPersonIds, CancellationToken ct)
    {
        var ids = seimasPersonIds.ToList();
        return await db.MpEmailPrefs.Where(p => ids.Contains(p.SeimasPersonId)).ToDictionaryAsync(p => p.SeimasPersonId, ct);
    }

    /// <summary>The keyed hashes among <paramref name="addresses"/> that are on the suppression list.</summary>
    public async Task<HashSet<string>> SuppressedAsync(NotificationsDb db, IEnumerable<string> addressesToCheck, CancellationToken ct)
    {
        var hashes = addressesToCheck.Select(keys.AddressHash).Distinct().ToList();
        return hashes.Count == 0
            ? []
            : (await db.Suppressions.Where(s => hashes.Contains(s.AddressHash)).Select(s => s.AddressHash).ToListAsync(ct)).ToHashSet();
    }

    public bool IsSuppressed(HashSet<string> suppressed, string? address) => address is not null && suppressed.Contains(keys.AddressHash(address));

    /// <summary>
    /// The MP's preferences link token (design 8.19.4): derived from the person number and the link's generation, so every
    /// email carries the same link; its hash is stored when the first email to the MP is queued.
    /// </summary>
    public async Task<string> PreferencesTokenAsync(NotificationsDb db, long seimasPersonId, int generation, CancellationToken ct) =>
        await TokenAsync(db, seimasPersonId, TokenKinds.Preferences, generation, ct);

    /// <summary>The extra recipient's own "Nebegauti kopijų" token, for the confirmed extra address of this generation.</summary>
    public async Task<string> ExtraStopTokenAsync(NotificationsDb db, long seimasPersonId, int generation, CancellationToken ct) =>
        await TokenAsync(db, seimasPersonId, TokenKinds.ExtraStop, generation, ct);

    /// <summary>Writes one MP email to the outbox in the caller's transaction.</summary>
    public async Task EnqueueAsync(NotificationsDb db, string kind, object payload, CancellationToken ct)
    {
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("MP mail is queued inside the change's transaction.");
        await outbox.EnqueueAsync(new OutboxMessage(kind, System.Text.Json.JsonSerializer.SerializeToElement(payload, payload.GetType(), System.Text.Json.JsonSerializerOptions.Web)), transaction, ct);
    }

    /// <summary>The latest version of the introduction letter that was sent (what an MP joining later gets).</summary>
    public static Task<IntroductionLetter?> SentLetterAsync(NotificationsDb db, CancellationToken ct) =>
        db.IntroductionLetters.AsNoTracking().Where(l => l.SentAt != null).OrderByDescending(l => l.Version).FirstOrDefaultAsync(ct);

    /// <summary>The introduction letter to one MP, with the MP's preferences link and one-click stop.</summary>
    public async Task EnqueueIntroductionAsync(NotificationsDb db, IntroductionLetter letter, MpRecipient mp, string address, MpEmailPref? pref, Guid runId, CancellationToken ct)
    {
        var token = await PreferencesTokenAsync(db, mp.SeimasPersonId, pref?.TokenGeneration ?? 0, ct);
        await EnqueueAsync(
            db,
            MpMailKinds.Introduction,
            new MpIntroductionEmail(
                address, letter.Version, letter.Subject, letter.Text, letter.SignerName, letter.SignerRole,
                MpMailPaths.Preferences(token), MpMailPaths.OneClick(token), Test: false, new DeliveryRef(runId, mp.Slug, Recipients.Official)),
            ct);
    }

    private async Task<string> TokenAsync(NotificationsDb db, long seimasPersonId, string kind, int generation, CancellationToken ct)
    {
        if (_tokens.TryGetValue((seimasPersonId, kind + "\n" + generation.ToString(System.Globalization.CultureInfo.InvariantCulture)), out var known))
        {
            return known;
        }

        var token = keys.DerivedToken(seimasPersonId, kind, generation);
        var hash = MailKeys.TokenHash(token);
        if (!await db.MpMailTokens.AnyAsync(t => t.TokenHash == hash, ct))
        {
            db.MpMailTokens.Add(new MpMailToken
            {
                Id = Ids.New(clock),
                SeimasPersonId = seimasPersonId,
                Kind = kind,
                TokenHash = hash,
                CreatedAt = clock.GetUtcNow(),
            });
        }

        _tokens[(seimasPersonId, kind + "\n" + generation.ToString(System.Globalization.CultureInfo.InvariantCulture))] = token;
        return token;
    }
}
