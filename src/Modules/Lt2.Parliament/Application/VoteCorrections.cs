using System.Text.Json;
using Lt2.Parliament.Domain;
using Lt2.Parliament.Import.Parsing;
using Lt2.Parliament.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Parliament.Application;

/// <summary>
/// <see cref="IVoteClassifications"/> (design 7.2): a moderator's correction of a vote's classification, kept apart from the
/// rules' result and written to the vote's <c>kind</c> and <c>stage</c> through <see cref="VoteClassification.Effective"/>, as
/// the import writes them. The vote's row is locked for the change, and the audit entry is written in the same transaction.
/// </summary>
internal sealed class VoteCorrections(IDbContextFactory<ParliamentDb> contexts, IClosingVotes closingVotes, IAuditLog audit, TimeProvider clock) : IVoteClassifications
{
    private static readonly Error NotFound = Error.NotFound("vote.not-found");

    public async Task<Result<VoteClassificationState>> StateAsync(long voteId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await ReadAsync(db, voteId, cancellationToken);
        if (row is null)
        {
            return NotFound;
        }

        var (vote, rules) = row.Value;
        return new VoteClassificationState(
            vote.Id, Codes.Of(vote.Kind), Codes.Of(vote.Stage), Codes.Of(rules.Kind), Codes.Of(rules.Stage),
            vote.KindOverride is null ? null : vote.OverriddenAt, vote.KindOverride is null ? null : vote.OverrideStatement,
            await closingVotes.ClosesARecordAsync(vote.Id, cancellationToken));
    }

    public async Task<Result<Unit>> CorrectAsync(long voteId, string kind, string? stage, string statement, StaffActor moderator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(moderator);
        if (StaffStatements.Check(statement) is { } invalid)
        {
            return invalid;
        }

        if (Codes.VoteKindOf(kind) is not { } chosenKind)
        {
            return Error.Validation(VoteClassification.KindInvalid, "kind");
        }

        var chosenStage = Codes.StageOf(stage);
        if (chosenKind == VoteKind.StageResult && chosenStage is null)
        {
            return Error.Validation(VoteClassification.StageRequired, "stage");
        }

        return await ChangeAsync(voteId, statement, moderator, (vote, rules, itemStage, closesRecord) =>
        {
            var requested = VoteClassification.Corrected(chosenKind, chosenStage, itemStage)!.Value;
            var current = new Classification(vote.Kind, vote.Stage);
            if (VoteClassification.CheckCorrection(current, rules, requested, closesRecord) is { } refused)
            {
                return Error.Conflict(refused);
            }

            vote.KindOverride = requested.Kind;
            vote.StageOverride = requested.Stage;
            vote.OverriddenAt = clock.GetUtcNow();
            vote.OverriddenBy = moderator.UserId;
            vote.OverrideStatement = statement.Trim();
            return "vote.reclassified";
        }, cancellationToken);
    }

    public async Task<Result<Unit>> ResetAsync(long voteId, string statement, StaffActor moderator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(moderator);
        if (StaffStatements.Check(statement) is { } invalid)
        {
            return invalid;
        }

        return await ChangeAsync(voteId, statement, moderator, (vote, rules, _, closesRecord) =>
        {
            if (VoteClassification.CheckReturn(vote.KindOverride is not null, rules, closesRecord) is { } refused)
            {
                return Error.Conflict(refused);
            }

            vote.KindOverride = null;
            vote.StageOverride = null;
            vote.OverriddenAt = null;
            vote.OverriddenBy = null;
            vote.OverrideStatement = null;
            return "vote.classification-reset";
        }, cancellationToken);
    }

    /// <summary>
    /// One change under the vote's row lock: <paramref name="change"/> refuses or sets the correction columns and names the
    /// audit action; the effective classification is then written from the rules and the correction, and audited with the
    /// classification before and after and the statement (nothing about any person).
    /// </summary>
    private async Task<Result<Unit>> ChangeAsync(
        long voteId, string statement, StaffActor moderator, Func<Domain.Vote, Classification, BillStage?, bool, Result<string>> change,
        CancellationToken cancellationToken)
    {
        // Asked before the transaction: the Appeals module's answer, not this schema's.
        var closesRecord = await closingVotes.ClosesARecordAsync(voteId, cancellationToken);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.Database.SqlQuery<long>($"SELECT id AS \"Value\" FROM parliament.vote WHERE id = {voteId} FOR UPDATE").ToListAsync(cancellationToken) is not [_])
        {
            return NotFound;
        }

        var vote = await db.Votes.FirstAsync(v => v.Id == voteId, cancellationToken);
        var itemStage = VoteRules.Stage(await db.AgendaItems.AsNoTracking().Where(i => i.Id == vote.AgendaItemId).Select(i => i.Stage).FirstOrDefaultAsync(cancellationToken));
        var rules = Rules(vote, itemStage);
        var before = new Classification(vote.Kind, vote.Stage);
        var outcome = change(vote, rules, itemStage, closesRecord);
        if (!outcome.IsSuccess)
        {
            return outcome.Error!;
        }

        var after = VoteClassification.Effective(rules, VoteClassification.CorrectionOf(vote.KindOverride, vote.StageOverride));
        vote.Kind = after.Kind;
        vote.Stage = after.Stage;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry(outcome.Value, moderator.UserId, moderator.Role, "vote:" + vote.Id,
                JsonSerializer.SerializeToElement(
                    new
                    {
                        before = new { kind = Codes.Of(before.Kind), stage = Codes.Of(before.Stage) },
                        after = new { kind = Codes.Of(after.Kind), stage = Codes.Of(after.Stage) },
                        statement = statement.Trim(),
                    },
                    JsonSerializerOptions.Web),
                moderator.IpHash),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>The rules' own classification of the stored vote, as the import makes it.</summary>
    private static Classification Rules(Domain.Vote vote, BillStage? itemStage)
    {
        var (kind, stage) = VoteRules.Classify(vote.Wording, itemStage);
        return new Classification(kind, stage);
    }

    private static async Task<(Domain.Vote Vote, Classification Rules)?> ReadAsync(ParliamentDb db, long voteId, CancellationToken cancellationToken)
    {
        var row = await (from v in db.Votes.AsNoTracking()
                         join i in db.AgendaItems on v.AgendaItemId equals i.Id
                         where v.Id == voteId
                         select new { v, i.Stage }).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.v, Rules(row.v, VoteRules.Stage(row.Stage)));
    }
}
