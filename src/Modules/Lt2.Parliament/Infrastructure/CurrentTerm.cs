using System.Linq.Expressions;
using Lt2.Parliament.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Parliament.Infrastructure;

/// <summary>The one reading of "the current term" and "a sitting MP", for the pages and for the summaries' recipients.</summary>
internal static class CurrentTerm
{
    /// <summary>The term whose MPs are shown: one with mandates imported, an open term before a closed one, then the latest.</summary>
    public static Task<Term?> FindAsync(ParliamentDb db, CancellationToken cancellationToken) =>
        db.Terms.AsNoTracking().Where(t => db.Mandates.Any(m => m.TermId == t.Id))
            .OrderByDescending(t => t.To == null).ThenByDescending(t => t.Id).FirstOrDefaultAsync(cancellationToken);

    /// <summary>A mandate that has not ended by <paramref name="today"/> (Vilnius): the MP is sitting.</summary>
    public static bool IsSitting(Mandate mandate, DateOnly today) => mandate.To is null || mandate.To >= today;

    /// <summary><see cref="IsSitting"/> for a database query.</summary>
    public static Expression<Func<Mandate, bool>> SittingOn(DateOnly today) => m => m.To == null || m.To >= today;
}