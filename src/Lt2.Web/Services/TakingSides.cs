using Lt2.Appeals;
using Lt2.Notifications;
using Lt2.SharedKernel;

namespace Lt2.Web.Services;

/// <summary>
/// Taking and withdrawing a side (design 8.12.1) with what follows from it, for every client of the API (the endpoints and
/// the server-side API alike). The host composes the Appeals module (the side) and the Notifications module (the follow)
/// through their public interfaces, as it composes <c>/me/bills</c> and <c>/me/follows</c>:
/// <list type="bullet">
/// <item>On a person's first side on a bill, the bill is followed for them in the same request (design 8.11.2, "Taking a
/// side follows the bill"). The side is saved first, in the Appeals module's own transaction, and is never refused or
/// undone by the follow: if following fails, the bill is simply not followed. Changing a side follows nothing; withdrawing
/// one unfollows nothing. The follow row holds nothing about the side, and nothing here logs a person, a bill or a side.</item>
/// <item>A side taken, changed or withdrawn marks the front page's copy of the agenda out of date (design 8.7, "Cached",
/// <see cref="FrontPageFreshness"/>); the Appeals module knows nothing of the web's cache.</item>
/// </list>
/// </summary>
internal sealed class TakingSides(IAppealsModule appeals, IFollows follows, FrontPageFreshness frontPage, ILogger<TakingSides> logger)
{
    public async Task<Result<Unit>> TakeAsync(string number, Guid userId, string side, CancellationToken cancellationToken)
    {
        var taken = await appeals.TakePositionAsync(number, userId, side, cancellationToken);
        if (!taken.IsSuccess)
        {
            return taken.Error!;
        }

        frontPage.MarkStale();
        if (taken.Value)
        {
            try
            {
                // Following again changes nothing; any refusal leaves the bill unfollowed.
                await follows.FollowBillAsync(userId, number, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The side is saved; only the kind of failure is logged, never who, which bill or which side.
                logger.LogWarning("Following a bill after a first side failed: {Failure}", ex.GetType().Name);
            }
        }

        return Result.Ok();
    }

    public async Task<Result<Unit>> ClearAsync(string number, Guid userId, CancellationToken cancellationToken)
    {
        var cleared = await appeals.ClearPositionAsync(number, userId, cancellationToken);
        if (cleared.IsSuccess)
        {
            frontPage.MarkStale();
        }

        return cleared;
    }
}
