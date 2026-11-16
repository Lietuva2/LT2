namespace Lt2.E2E;

/// <summary>
/// What the product keeps on purpose when an account is deleted or an organisation closed, and an end-to-end test must not
/// leave on the shared stack: removed by SQL, because the product offers no way to remove it (design 6.9: what a moderator
/// did stands; design 8.14.14: a closed organisation stays). Each statement touches only rows that hang from the test's own
/// accounts, organisations or moderators, never the demonstration data or the accounts people use by hand.
/// </summary>
internal static class E2EData
{
    /// <summary>
    /// Before the test's accounts (<paramref name="emails"/>, quoted for SQL) are deleted: the reports, moderators' actions and
    /// histories on their arguments and on the organisations they registered or alone represent. Returns those organisations,
    /// deleted after the accounts (<see cref="DeleteOrganisationsAsync"/>: an account registered with an organisation's code
    /// still refers to the code until then).
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> ForgetAsync(string emails)
    {
        var output = await E2ESteps.SqlAsync(
            $"""
            begin;
            create temp table t_user on commit drop as select id from identity."user" where lower(email) in ({emails});
            create temp table t_org on commit drop as select id from appeals.organisation
             where registered_by_user_id in (select id from t_user)
                or (id in (select organisation_id from appeals.organisation_representative where user_id in (select id from t_user))
                    and id not in (select organisation_id from appeals.organisation_representative where user_id not in (select id from t_user)));
            create temp table t_reason on commit drop as select id from appeals.reason where author_user_id in (select id from t_user);
            delete from appeals.report where target_kind = 'reason' and target_id in (select id from t_reason);
            delete from appeals.moderation_action where target_kind = 'reason' and target_id in (select id from t_reason);
            delete from appeals.revision where entity_kind = 'reason' and entity_id in (select id from t_reason);
            select id from t_org;
            commit;
            """);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => Guid.TryParse(l, out _)).Select(Guid.Parse).ToList();
    }

    /// <summary>
    /// Organisations a test registered (closed by then, or closed with their last representative's account), with their
    /// statements, codes, links and representatives (the foreign keys cascade) and the reports, moderators' actions,
    /// histories, follows and follow events on them, as the old clean-up scripts outside the repository removed "(E2E)" organisations.
    /// </summary>
    public static async Task DeleteOrganisationsAsync(IEnumerable<Guid> organisations)
    {
        var ids = string.Join(",", organisations.Select(o => $"'{o}'"));
        if (ids.Length == 0)
        {
            return;
        }

        await E2ESteps.SqlAsync(
            $"""
            begin;
            create temp table d_org on commit drop as select id from appeals.organisation where id in ({ids});
            create temp table d_statement on commit drop as select id from appeals.organisation_statement where organisation_id in (select id from d_org);
            delete from appeals.report where (target_kind = 'organisation' and target_id in (select id from d_org))
               or (target_kind = 'statement' and target_id in (select id from d_statement));
            delete from appeals.moderation_action where (target_kind = 'organisation' and target_id in (select id from d_org))
               or (target_kind = 'statement' and target_id in (select id from d_statement));
            delete from appeals.revision where (entity_kind = 'organisation' and entity_id in (select id from d_org))
               or (entity_kind = 'statement' and entity_id in (select id from d_statement));
            delete from identity.invitation where kind = 'organisation' and organisation_id in (select id from d_org);
            delete from notify.follow where kind = 'organisation' and key in (select id::text from d_org);
            delete from notify.bill_event where kind = 'statement' and key in (select id::text from d_statement);
            delete from appeals.organisation where id in (select id from d_org);
            commit;
            """);
    }

    /// <summary>
    /// A demonstration bill's description goes back to what it was before a test wrote or corrected it with
    /// <see cref="AppealFlowTests.Marker"/> (no description, or on XVP-0533 the invented AI summary, which the migrator's
    /// repeatable <c>--demo-appeal</c> writes again): the test's texts, their history, the reports on the description and
    /// the confirmations go. The product cannot do this (a description a person wrote stands for its whole life, design 8.13).
    /// Used before and, in a <c>finally</c>, after a test that changes a description.
    /// </summary>
    public static async Task RestoreDescriptionAsync(string bill)
    {
        await E2ESteps.SqlAsync(
            $"""
            begin;
            create temp table own on commit drop as select id from appeals.bill_record where bill_number = '{bill}';
            delete from appeals.report where target_kind = 'record' and target_id in (select id from own);
            delete from appeals.moderation_action where target_kind = 'record' and action in ('confirm', 'describe', 'undescribe') and target_id in (select id from own);
            delete from appeals.revision where entity_kind = 'record' and field = 'description' and entity_id in (select id from own)
              and (old_value like '%{AppealFlowTests.Marker}%' or new_value like '%{AppealFlowTests.Marker}%');
            -- The invented summary's own history goes with the test's text: the migrator writes both again, and a second copy
            -- would be left over by every run.
            delete from appeals.revision where entity_kind = 'record' and field = 'description' and changed_by_user_id is null
              and new_value like 'Projektas leistų savivaldybėms pačioms nustatyti%'
              and entity_id in (select id from appeals.bill_record where id in (select id from own) and description like '%{AppealFlowTests.Marker}%');
            update appeals.bill_record set description = '', description_source = null, description_url = null, description_at = null
             where id in (select id from own) and description like '%{AppealFlowTests.Marker}%';
            update appeals.bill_record set description_source = 'ai'
             where id in (select id from own) and description like 'Projektas leistų savivaldybėms pačioms nustatyti%';
            commit;
            """);
        if (bill == "XVP-0533")
        {
            await Compose.RunAsync("run", "--rm", "--no-deps", "migrator", "--demo-appeal");
        }
    }

    /// <summary>
    /// A summary a test made (an urgent dry run), with its items and deliveries (the foreign keys cascade): summaries are
    /// never deleted by the product, and one left behind would count for the bill's week (one urgent summary a week).
    /// </summary>
    public static Task DeleteSummaryAsync(Guid run) =>
        E2ESteps.SqlAsync($"delete from notify.digest_run where id = '{run}'");

    /// <summary>
    /// What a staff fixture's moderator or administrator did, before its account is deleted: moderators' actions stand after
    /// an account is gone (design 6.9), and a test's actions on the demonstration bills must not.
    /// </summary>
    public static Task ForgetStaffAsync(string email) =>
        E2ESteps.SqlAsync(
            $"""
            delete from appeals.moderation_action where moderator_user_id = (select id from identity."user" where lower(email) = '{email.ToLowerInvariant()}');
            """);
}
