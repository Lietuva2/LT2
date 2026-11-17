-- What end-to-end tests could leave on the local stack, counted (tools/run_tests.py prints it before and after a run;
-- every line should read the same). A test that leaves something here has not cleaned up after itself (AGENTS.md,
-- "Writing tests"). Demonstration data and the accounts people use by hand are counted only where a test changes them.
select 'test accounts (@pavyzdys.test, not demo.*) ' || count(*) from identity."user" where lower(email) like '%@pavyzdys.test' and lower(email) not like 'demo.%';
select 'stand-in MPs'' accounts (@lrs.lt) ' || count(*) from identity."user" where lower(email) like '%@lrs.lt';
select 'run invitations (E2E-RUN-*) ' || count(*) from identity.invitation where code like 'E2E-RUN-%';
select 'organisations (E2E) ' || count(*) from appeals.organisation where name like '%(E2E)%';
select 'organisations ' || count(*) from appeals.organisation;
select 'statements ' || count(*) from appeals.organisation_statement;
select 'reasons ' || count(*) from appeals.reason;
select 'positions ' || count(*) from appeals.position;
select 'reports ' || count(*) from appeals.report;
select 'moderators'' actions ' || count(*) from appeals.moderation_action;
select 'revisions ' || count(*) from appeals.revision;
select 'writing restrictions ' || count(*) from appeals.writing_restriction;
select 'MP responses ' || count(*) from appeals.mp_response;
select 'records ' || string_agg(bill_number || ' ' || status || ' ' || coalesce(description_source, '-') || case when description like '%(E2E bandymas)%' then ' (E2E text)' else '' end, ', ' order by bill_number) from appeals.bill_record;
select 'feedback ' || count(*) from identity.feedback;
select 'follows ' || count(*) from notify.follow;
-- Without the follow job's comment records (kind comment): it makes them from every comment it reads, those written before the run
-- too, and they are where its next window starts (design 8.23.14), so no test may remove one (E2EData.ForgetSinceAsync). 'bill events (not comments) ' || count(*) from notify.bill_event where kind <> 'comment';
select 'summaries ' || count(*) || ' (' || coalesce(string_agg(distinct status, ','), '') || ')' from notify.digest_run;
select 'MP mail: letters ' || (select count(*) from notify.introduction_letter) || ', choices ' || (select count(*) from notify.mp_email_pref)
    || ', tokens ' || (select count(*) from notify.mp_mail_token) || ', suppressed ' || (select count(*) from notify.suppression);
select 'settings ' || coalesce(string_agg(key || '=' || value::text, ', ' order by key), '') from config.setting;
select 'outbox ' || count(*) from notify.outbox;
select 'staff role activations ' || count(*) from identity.role_activation;
