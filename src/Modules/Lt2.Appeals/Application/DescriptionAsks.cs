using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// "Prašau aprašymo" (design 8.13.2, owner 2026-11-27): a signed-in member asks for the description of a bill that shows
/// none, open or finished, once per bill, and may take the ask back. Only the count is public; who asked is shown to
/// nobody, administrators included. Asks are written under the bill's lock, which a moderator's description takes too, so
/// an ask never lands after the description that clears them (<see cref="ClearAsync"/>).
/// </summary>
internal sealed class DescriptionAsks(IDbContextFactory<AppealsDb> contexts, IBillCatalogue bills, TimeProvider clock) : IDescriptionAsks
{
    public async Task<Result<DescriptionAskState>> StateAsync(string billNumber, Guid? userId, CancellationToken cancellationToken)
    {
        var number = AppealReads.NormaliseBillNumber(billNumber);
        if (await bills.FindAsync(number, cancellationToken) is null)
        {
            return Error.NotFound("bill.not-found");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var record = await AppealReads.FindRecordAsync(db, number, tracking: false, cancellationToken);
        var count = await db.DescriptionAsks.CountAsync(a => a.BillNumber == number, cancellationToken);
        bool? asked = userId is { } id ? await db.DescriptionAsks.AnyAsync(a => a.BillNumber == number && a.UserId == id, cancellationToken) : null;
        return new DescriptionAskState(count, asked, UndescribedRules.CheckAsk(record?.Status, record?.Description) is null);
    }

    public async Task<Result<Unit>> AskAsync(string billNumber, Guid userId, CancellationToken cancellationToken)
    {
        var number = AppealReads.NormaliseBillNumber(billNumber);
        var bill = await bills.FindAsync(number, cancellationToken);
        if (bill is null)
        {
            return Error.NotFound("bill.not-found");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AdvisoryLocks.BillAsync(db, bill.Number, cancellationToken);
        var record = await AppealReads.FindRecordAsync(db, bill.Number, tracking: false, cancellationToken);
        if (UndescribedRules.CheckAsk(record?.Status, record?.Description) is { } refused)
        {
            return RuleErrors.Of(refused);
        }

        if (!await db.DescriptionAsks.AnyAsync(a => a.BillNumber == bill.Number && a.UserId == userId, cancellationToken))
        {
            db.DescriptionAsks.Add(new DescriptionAsk { UserId = userId, BillNumber = bill.Number, AskedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> WithdrawAsync(string billNumber, Guid userId, CancellationToken cancellationToken)
    {
        var number = AppealReads.NormaliseBillNumber(billNumber);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.DescriptionAsks.Where(a => a.BillNumber == number && a.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// A person's description was saved, or an AI summary confirmed: the bill no longer wants one, so its asks have done their
    /// work and go (nothing kept that is no longer needed). In the caller's transaction, under the bill's lock.
    /// </summary>
    public static Task ClearAsync(AppealsDb db, string billNumber, CancellationToken cancellationToken)
    {
        var number = AppealReads.NormaliseBillNumber(billNumber);
        return db.DescriptionAsks.Where(a => a.BillNumber == number).ExecuteDeleteAsync(cancellationToken);
    }
}
