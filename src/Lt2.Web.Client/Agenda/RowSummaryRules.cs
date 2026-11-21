using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Digests;

namespace Lt2.Web.Client.Agenda;

/// <summary>
/// What a list row (the front page, the agenda, <c>/projektai</c>) says in place of "riba pasiekta" for a bill whose threshold
/// is reached (design 8.12.2, owner 2026-11-21), the first that holds: on an open record the day the next summary that would
/// hold it is planned ("suvestinė planuojama …"); else the day the last summary really sent to MPs that held it went
/// ("suvestinė išsiųsta …"); else nothing, and the row says "riba pasiekta". A closed record has no next summary, only the
/// last one sent. No trial variants: a planned day is shown whatever the sending mode, and a dry run never counts as sent.
/// A row under its threshold, and a hidden record, name no summary.
/// </summary>
public static class RowSummaryRules
{
    public static RecordSummaryResponse? Of(string status, bool reached, DateOnly? planned, DateOnly? lastSent)
    {
        if (!reached || status is not (BillRecordStatuses.Open or BillRecordStatuses.Closed))
        {
            return null;
        }

        if (status == BillRecordStatuses.Open && planned is { } next)
        {
            return new RecordSummaryResponse(RowSummaryKinds.Planned, next);
        }

        return lastSent is { } sent ? new RecordSummaryResponse(RowSummaryKinds.Sent, sent) : null;
    }
}
