using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>Design 8.3: the account behind an argument, a comment or an organisation's statement, as the writing restriction finds it.</summary>
internal sealed class AuthorLookup(IDbContextFactory<AppealsDb> contexts) : IAuthorLookup
{
    public async Task<Result<Guid>> AuthorOfAsync(string kind, Guid id, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var (who, notFound) = kind switch
        {
            "reason" => (db.Reasons.Where(r => r.Id == id).Select(r => (Guid?)r.AuthorUserId), "reason.not-found"),
            "comment" => (db.Comments.Where(c => c.Id == id).Select(c => (Guid?)c.AuthorUserId), "comment.not-found"),
            "statement" => (db.OrganisationStatements.Where(s => s.Id == id).Select(s => (Guid?)s.WrittenByUserId), "statement.not-found"),
            _ => (null, "lock.target"),
        };
        if (who is null)
        {
            return Error.Validation("lock.target", "targetKind");
        }

        return await who.FirstOrDefaultAsync(cancellationToken) is { } userId ? userId : Error.NotFound(notFound);
    }
}