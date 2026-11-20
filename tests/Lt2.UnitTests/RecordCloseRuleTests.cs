using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals.Domain;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.1 step 6 (owner, 2026-11-19): a moderator closes a record because the Seimas rejected the bill or its initiators
/// withdrew it, with the day and the lrs.lt source; only such a close can be undone.
/// </summary>
public sealed class RecordCloseRuleTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);
    private const string Source = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/pavyzdys";

    private static readonly IStringLocalizer<Lt2.Api.Contracts.ValidationMessages> L = new StringLocalizer<Lt2.Api.Contracts.ValidationMessages>(
        new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    [Fact]
    public void A_moderator_closes_an_open_record_as_rejected_or_withdrawn_with_the_day_the_source_and_a_statement()
    {
        RecordRules.CheckClose(BillRecordStatuses.Open, false, ClosingReasons.Rejected, Today, Today, Source, "Seimas atmetė.").ShouldBeNull();
        RecordRules.CheckClose(BillRecordStatuses.Open, false, ClosingReasons.Withdrawn, Today.AddDays(-30), Today, Source, "Atsiimtas.").ShouldBeNull();
    }

    [Theory]
    [InlineData(" ", "rejected", 0, Source, "statement.required")]
    [InlineData("Bandymas.", "vote", 0, Source, "closing.reason")]
    [InlineData("Bandymas.", "", 0, Source, "closing.reason")]
    [InlineData("Bandymas.", "returned", 0, Source, "closing.reason")]
    [InlineData("Bandymas.", "rejected", 1, Source, "closing.date-future")]
    [InlineData("Bandymas.", "rejected", 0, "", "closing-url.invalid")]
    [InlineData("Bandymas.", "rejected", 0, "http://lrs.lt/a", "closing-url.invalid")]
    [InlineData("Bandymas.", "rejected", 0, "https://lrs.lt.pavyzdys.test/a", "closing-url.invalid")]
    [InlineData("Bandymas.", "rejected", 0, "https://naujienos.pavyzdys.test/a", "closing-url.invalid")]
    public void A_close_needs_a_statement_a_moderators_reason_a_day_not_in_the_future_and_a_source_on_lrs_lt(
        string statement, string reason, int daysAfterToday, string source, string expected) =>
        RecordRules.CheckClose(BillRecordStatuses.Open, false, reason, Today.AddDays(daysAfterToday), Today, source, statement).ShouldBe(expected);

    [Fact]
    public void A_close_without_a_day_is_refused() =>
        RecordRules.CheckClose(BillRecordStatuses.Open, false, ClosingReasons.Rejected, default, Today, Source, "Bandymas.").ShouldBe("closing.date-required");

    [Theory]
    [InlineData("closed", false)]
    [InlineData("rejected", false)]
    [InlineData("open", true)]
    public void Only_an_open_record_whose_final_vote_is_not_recorded_is_closed_by_a_moderator(string status, bool finished) =>
        RecordRules.CheckClose(status, finished, ClosingReasons.Rejected, Today, Today, Source, "Bandymas.").ShouldBe("moderation.wrong-state");

    [Theory]
    [InlineData("closed", "rejected", true)]
    [InlineData("closed", "withdrawn", true)]
    [InlineData("closed", "vote", false)]
    [InlineData("closed", null, false)]
    [InlineData("open", null, false)]
    [InlineData("rejected", "withdrawn", false)]
    public void Only_a_record_a_moderator_closed_is_reopened_never_one_closed_by_a_vote(string status, string? reason, bool reopens)
    {
        RecordRules.MayReopen(status, reason).ShouldBe(reopens);
        RecordRules.CheckReopen(status, reason, "Klaida.").ShouldBe(reopens ? null : "moderation.wrong-state");
    }

    [Fact]
    public void A_reopening_needs_a_statement() =>
        RecordRules.CheckReopen(BillRecordStatuses.Closed, ClosingReasons.Rejected, "").ShouldBe("statement.required");

    [Fact]
    public void The_source_is_required_and_follows_the_official_texts_rule()
    {
        RecordRules.CheckClosingUrl(null).ShouldBe("closing-url.invalid");
        RecordRules.CheckClosingUrl("  ").ShouldBe("closing-url.invalid");
        RecordRules.CheckClosingUrl(Source).ShouldBeNull();
        RecordRules.CheckClosingUrl("https://lrs.lt/" + new string('a', RecordRules.ClosingUrlMaxLength)).ShouldBe("closing-url.invalid");
        RecordRules.ClosingUrlMaxLength.ShouldBe(AppealLimits.DescriptionUrlMaxLength);
    }

    [Fact]
    public void A_close_freezes_all_four_counts_and_a_reopening_clears_everything_of_it()
    {
        var record = new BillRecord { Status = BillRecordStatuses.Open };
        var now = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        var counts = CountRules.Publish(7, 2, 4, 1);
        ClosingRules.Close(record, counts, now, ClosingReasons.Withdrawn, null, Today, Source);
        (record.Status, record.ClosedAt, record.ClosingVoteId, record.ClosedReason, record.ClosedDecidedOn, record.ClosedSourceUrl)
            .ShouldBe((BillRecordStatuses.Closed, (DateTimeOffset?)now, (long?)null, ClosingReasons.Withdrawn, (DateOnly?)Today, Source));
        ClosingRules.IsFrozen(record).ShouldBeTrue();
        ClosingRules.Shown(record, Lt2.Appeals.SideCounts.None).ShouldBe(counts);

        ClosingRules.Reopen(record, now.AddHours(1));
        (record.Status, record.ClosedAt, record.ClosedReason, record.ClosedDecidedOn, record.ClosedSourceUrl).ShouldBe((BillRecordStatuses.Open, (DateTimeOffset?)null, (string?)null, (DateOnly?)null, (string?)null));
        ClosingRules.IsFrozen(record).ShouldBeFalse();
        record.ClosedFor.ShouldBeNull();
        var live = CountRules.Publish(9, 3, 4, 1);
        ClosingRules.Shown(record, live).ShouldBe(live, "the live counts come back");
    }

    [Theory]
    [InlineData("rejected", "2026-11-17", Source, "Bandymas.", true)]
    [InlineData("withdrawn", "2026-11-17", Source, "Bandymas.", true)]
    [InlineData("vote", "2026-11-17", Source, "Bandymas.", false)]
    [InlineData("rejected", null, Source, "Bandymas.", false)]
    [InlineData("rejected", "2026-11-17", "", "Bandymas.", false)]
    [InlineData("rejected", "2026-11-17", "https://pavyzdys.test/a", "Bandymas.", false)]
    [InlineData("rejected", "2026-11-17", Source, " ", false)]
    public void The_close_requests_validator_holds_the_reason_the_day_the_source_and_the_statement(string reason, string? day, string source, string statement, bool valid)
    {
        var validator = new CloseRecordRequestValidator(L);
        var request = new CloseRecordRequest
        {
            Reason = reason,
            DecidedOn = day is null ? default : DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture),
            SourceUrl = source,
            Statement = statement,
        };
        validator.Validate(request).IsValid.ShouldBe(valid);
    }
}
