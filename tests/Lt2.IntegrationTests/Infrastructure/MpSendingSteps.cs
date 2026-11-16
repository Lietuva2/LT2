using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts.Digests;
using Lt2.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>An MP email waiting in the outbox (design 8.19.3): its kind, recipient address and payload.</summary>
public sealed record OutboxMpMail(string Kind, string To, JsonElement Payload);

/// <summary>
/// Steps of design 8.19 (real sending). The MP mail tables (choices, links, the letter, the suppression list) are shared by
/// the run, so every test about sending starts from <see cref="ResetMpMailAsync"/> and calls it again in its <c>finally</c>,
/// with <c>Digest:Sending</c> put back; summaries keep to their own weeks (<see cref="ReasonsToComeSteps.MondayInWeeks"/>).
/// </summary>
public static class MpSendingSteps
{
    public static readonly string[] MpMailKinds = ["email.mp-digest", "email.mp-introduction", "email.mp-mail-notice"];

    public const string Letter = "Gerbiami Seimo nariai,\n\nLietuva 2.0 – vieta, kur žmonės nurodo, kaip prašo balsuoti dėl projektų.\n\nSuvestinės ateis kartą per savaitę.";

    /// <summary>
    /// Forgets everything MP mail left: the choices, links, suppression entries, letters, introduction runs and every summary
    /// a test let go (their deliveries cascade), and the MP mail waiting in the outbox (it holds addresses).
    /// </summary>
    public static async Task ResetMpMailAsync(this DatabaseFixture database)
    {
        await database.ExecuteAsync(
            """
            DELETE FROM notify.mp_email_pref;
            DELETE FROM notify.mp_mail_token;
            DELETE FROM notify.suppression;
            DELETE FROM notify.introduction_letter;
            DELETE FROM notify.digest_run WHERE kind = 'introduction' OR status IN ('waiting', 'sent', 'cancelled', 'expired');
            DELETE FROM notify.outbox WHERE kind = ANY(@kinds);
            """,
            ("kinds", MpMailKinds));
    }

    /// <summary>Writes the letter and sends it to every sitting MP (a fresh passkey confirmation first); returns how many got it.</summary>
    public static async Task<int> IntroduceAsync(this StaffMember administrator, string subject = "Lietuva 2.0 – kas tai ir ko tikėtis")
    {
        using (var saved = await administrator.Browser.SendAsync(HttpMethod.Put, MpSendingAdminRoutes.Introduction, new IntroductionRequest
        {
            Subject = subject, Text = Letter, SignerName = "Rūta Mockutė", SignerRole = "VšĮ LT2.0 vadovė",
        }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using (var confirmed = await administrator.Browser.ConfirmPasskeyAsync(administrator.Authenticator))
        {
            confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await confirmed.Content.ReadAsStringAsync());
        }

        using var sent = await administrator.Browser.PostAsync(MpSendingAdminRoutes.IntroductionSend, new { });
        sent.StatusCode.ShouldBe(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        return (await sent.ReadAsync<IntroductionSentResponse>()).Mps;
    }

    /// <summary>"Siųsti" on a waiting summary, after the fresh passkey confirmation it needs (design 6.2, 8.19.2).</summary>
    public static async Task<HttpResponseMessage> SendDigestAsync(this StaffMember staff, Guid runId)
    {
        using (var confirmed = await staff.Browser.ConfirmPasskeyAsync(staff.Authenticator))
        {
            confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await confirmed.Content.ReadAsStringAsync());
        }

        return await staff.Browser.PostAsync(DigestRoutes.SendOf(runId), new { });
    }

    /// <summary>Sets a runtime setting after a fresh passkey confirmation (design 6.9), for tests longer than ten minutes.</summary>
    public static async Task SetFreshAsync(this StaffMember administrator, string key, object value)
    {
        using (var confirmed = await administrator.Browser.ConfirmPasskeyAsync(administrator.Authenticator))
        {
            confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await confirmed.Content.ReadAsStringAsync());
        }

        await administrator.Browser.SetSettingAsync(key, value);
    }

    public static async Task ResetFreshAsync(this StaffMember administrator, string key)
    {
        using (var confirmed = await administrator.Browser.ConfirmPasskeyAsync(administrator.Authenticator))
        {
            confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await confirmed.Content.ReadAsStringAsync());
        }

        await administrator.Browser.ResetSettingAsync(key);
    }

    /// <summary>The MP mail in the outbox, optionally to one address and of one kind.</summary>
    public static async Task<List<OutboxMpMail>> MpMailAsync(this DatabaseFixture database, string? to = null, string? kind = null)
    {
        var rows = new List<OutboxMpMail>();
        await using var connection = new Npgsql.NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            """
            SELECT kind, payload->>'to', payload::text FROM notify.outbox
            WHERE kind = ANY(@kinds) AND (@to::text IS NULL OR lower(payload->>'to') = lower(@to::text)) AND (@kind::text IS NULL OR kind = @kind::text)
            ORDER BY created_at, id
            """,
            connection);
        command.Parameters.AddWithValue("kinds", MpMailKinds);
        command.Parameters.AddWithValue("to", (object?)to ?? DBNull.Value);
        command.Parameters.AddWithValue("kind", (object?)kind ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new OutboxMpMail(reader.GetString(0), reader.GetString(1), JsonDocument.Parse(reader.GetString(2)).RootElement.Clone()));
        }

        return rows;
    }

    /// <summary>The token in the newest MP email's preferences link to <paramref name="to"/>.</summary>
    public static async Task<string> PreferencesTokenAsync(this DatabaseFixture database, string to)
    {
        var mails = await database.MpMailAsync(to);
        var path = mails.Select(m => m.Payload.TryGetProperty("preferencesPath", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()
                : m.Payload.TryGetProperty("path", out var q) && q.ValueKind == JsonValueKind.String && q.GetString()!.StartsWith(MpMailRoutes.PreferencesPage + "/", StringComparison.Ordinal) && !q.GetString()!.Contains("/patvirtinti/", StringComparison.Ordinal) ? q.GetString() : null)
            .LastOrDefault(p => p is not null);
        path.ShouldNotBeNull($"an MP email with the preferences link to {to} is in the outbox");
        return path[(path.LastIndexOf('/') + 1)..];
    }

    /// <summary>The token of a page path in the newest MP notice of <paramref name="notice"/> to <paramref name="to"/>.</summary>
    public static async Task<string> NoticeTokenAsync(this DatabaseFixture database, string to, string notice)
    {
        var mail = (await database.MpMailAsync(to, "email.mp-mail-notice")).LastOrDefault(m => m.Payload.GetProperty("notice").GetString() == notice);
        mail.ShouldNotBeNull($"a notice {notice} to {to}");
        var path = mail.Payload.GetProperty("path").GetString()!;
        return path[(path.LastIndexOf('/') + 1)..];
    }

    public static async Task<IReadOnlyList<DigestRunInfo>> RunDueAsync(this Lt2Factory factory, DateTimeOffset now)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDigests>().RunDueAsync(now, CancellationToken.None);
    }

    /// <summary>A Vilnius time on a day, as the summaries' clock sees it.</summary>
    public static DateTimeOffset At(DateOnly day, int hour, int minute = 0)
    {
        var local = day.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, Lt2.SharedKernel.VilniusTime.Zone.GetUtcOffset(local));
    }

    /// <summary>The status of one MP's own delivery in a run.</summary>
    public static Task<string?> DeliveryAsync(this DatabaseFixture database, Guid runId, long seimasPersonId, string recipient = "official") =>
        database.ScalarAsync<string>(
            "SELECT status FROM notify.digest_delivery WHERE run_id = @r AND seimas_person_id = @p AND recipient = @x",
            ("r", runId), ("p", seimasPersonId), ("x", recipient));
}
