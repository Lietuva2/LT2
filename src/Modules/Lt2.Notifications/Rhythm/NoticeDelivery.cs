using Lt2.Notifications.Follows;
using Lt2.Notifications.Infrastructure;
using Lt2.Notifications.MpMail;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Notifications.Rhythm;

/// <summary>What one delivery wrote: things told and emails written to the outbox.</summary>
internal sealed record DeliveryOutcome(int Notices, int Emails);

/// <summary>
/// The one place that decides, for every member and every thing a kind has for them, whether it goes now or waits, and writes
/// the emails (design 8.24). Each run makes, in this order, the run's emails ("Iš karto"), the "planned" dates that cannot wait,
/// the daily email when its morning has come and the weekly one on Monday; then it gathers everything for one person into one
/// email. Nothing here knows a kind: each <see cref="IMemberNoticeKind"/> says what it has for a pass.
/// </summary>
internal sealed class NoticeDelivery(
    IDbContextFactory<NotificationsDb> contexts,
    IEnumerable<IMemberNoticeKind> kinds,
    IParticipants participants,
    ISettings settings,
    IOutbox outbox,
    MailKeys keys,
    TimeProvider clock)
{
    /// <summary>
    /// The follows screen's choice of how often, where "Keisti periodiškumą" in the footer of every email of the rhythm links
    /// (design 8.24.1, owner 2026-11-17; the switches are on the same screen above it).
    /// </summary>
    public const string AccountPath = AccountScreens.EmailRhythm;

    public async Task<int> RecordAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var recorded = 0;
        foreach (var kind in kinds)
        {
            recorded += await kind.RecordAsync(now, cancellationToken);
        }

        return recorded;
    }

    public async Task<DeliveryOutcome> DeliverAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var morningAt = FollowRules.ResponsesAt(await settings.GetAsync(SettingKeys.FollowsResponsesAt, cancellationToken));
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // One delivery at a time, so two runs at once never tell one thing twice.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["lt2.follows.delivery"], cancellationToken);
        var choices = await db.NoticeSettings.AsNoTracking().ToDictionaryAsync(n => n.UserId, cancellationToken);
        Pass[] passes = [Pass.Immediate, Pass.Early, Pass.Daily, Pass.Weekly];
        var due = new List<(Pass Pass, DueNotice Notice)>();
        foreach (var which in passes)
        {
            DateTimeOffset? cut = which switch
            {
                Pass.Daily => RhythmRules.DailyCut(now, morningAt),
                Pass.Weekly => RhythmRules.WeeklyCut(now, morningAt),
                _ => null,
            };

            // At most one daily email a Vilnius day and one weekly email a week (design 8.24): a period's pass is made once,
            // whatever hour Follows:ResponsesAt names later that day; a missed one is made at the next run (a catch-up).
            if (cut is { } periodCut)
            {
                var kind = which == Pass.Daily ? Frequencies.Daily : Frequencies.Weekly;
                var period = VilniusTime.DateOf(periodCut);
                if (await db.RhythmPasses.AnyAsync(r => r.Pass == kind && r.Period == period, cancellationToken))
                {
                    continue;
                }

                db.RhythmPasses.Add(new RhythmPassMade { Pass = kind, Period = period, MadeAt = now });
            }
            var pass = new NoticePass(which, now, cut, morningAt, db, choices);
            foreach (var kind in kinds)
            {
                due.AddRange((await kind.TakeAsync(pass, cancellationToken)).Select(n => (which, n)));
            }

            // Each pass's marks are saved before the next one reads, in the same transaction.
            await db.SaveChangesAsync(cancellationToken);
        }

        var emails = await WriteAsync(db, due, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DeliveryOutcome(due.Count, emails);
    }

    /// <summary>
    /// One email per person per run (design 8.24): one thing alone is the email it always was; more make the gathered email.
    /// Only members get anything (an account deleted or pending meanwhile gets nothing). Every email carries the account's stop
    /// link (design 8.24.1): its token is derived, and its hash is stored with the first email the account is written.
    /// </summary>
    private async Task<int> WriteAsync(NotificationsDb db, List<(Pass Pass, DueNotice Notice)> due, IDbContextTransaction transaction, CancellationToken ct)
    {
        if (due.Count == 0)
        {
            return 0;
        }

        var people = due.Select(d => d.Notice.UserId).Distinct().ToList();
        var members = await participants.FindManyAsync(people, ct);
        var linked = (await db.MemberMailTokens.Where(t => people.Contains(t.UserId)).Select(t => t.UserId).ToListAsync(ct)).ToHashSet();
        var written = 0;
        foreach (var person in due.GroupBy(d => d.Notice.UserId))
        {
            // Design 8.3: a locked account's follows send nothing.
            if (members.GetValueOrDefault(person.Key) is not { IsLocked: false } member)
            {
                continue;
            }

            var address = new NoticeAddress(member.Email, StopToken(db, person.Key, linked));
            var messages = person.Select(d => d.Notice.Write(address)).ToList();
            var message = messages.Count == 1
                ? messages[0]
                : OutboxMessage.Create(MessageKinds.GatheredNoticesEmail, new GatheredNoticesEmail(
                    member.Email, RhythmOf(person.Select(d => d.Pass)), messages.Select(m => new GatheredNotice(m.Kind, m.Payload)).ToList(), AccountPath, address.StopToken));
            await outbox.EnqueueAsync(message, transaction.GetDbTransaction(), ct);
            written++;
        }

        await db.SaveChangesAsync(ct);
        return written;
    }

    /// <summary>The account's stop link token (generation 0); its hash is added to the run's context when the account has none.</summary>
    private string StopToken(NotificationsDb db, Guid userId, HashSet<Guid> linked)
    {
        var token = keys.MemberStopToken(userId, 0);
        if (linked.Add(userId))
        {
            db.MemberMailTokens.Add(new MemberMailToken { UserId = userId, TokenHash = MailKeys.TokenHash(token), Generation = 0, CreatedAt = clock.GetUtcNow() });
        }

        return token;
    }

    /// <summary>Which email a gathered one is, for its subject: the weekly or the daily one when it is one of them.</summary>
    private static string RhythmOf(IEnumerable<Pass> passes)
    {
        var all = passes.ToHashSet();
        return all.Contains(Pass.Weekly) ? Frequencies.Weekly : all.Contains(Pass.Daily) ? Frequencies.Daily : Frequencies.Immediate;
    }
}
