using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <summary>
    /// Design 8.12, "Its name" (decided by the owner on 2026-11-15): the per-bill row is the bill's record. Everything is
    /// renamed in place, so no row is copied or lost: the table <c>appeal</c> becomes <c>bill_record</c> with its key,
    /// check, indexes and the foreign keys that point at it, the columns <c>appeal_id</c> become <c>record_id</c>, and the
    /// stored kind <c>appeal</c> (reports, revisions, moderation actions) becomes <c>record</c>. The audit log is
    /// append-only and keeps its old entries as they were written.
    /// </summary>
    public partial class BillRecordNames : Migration
    {
        private static readonly string[] Referencing = ["daily_stat", "endorsement", "flag", "position", "reason"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "appeal", schema: "appeals", newName: "bill_record", newSchema: "appeals");
            migrationBuilder.Sql("ALTER TABLE appeals.bill_record RENAME CONSTRAINT pk_appeal TO pk_bill_record;");
            migrationBuilder.Sql("ALTER TABLE appeals.bill_record RENAME CONSTRAINT ck_appeal_threshold_positive TO ck_bill_record_threshold_positive;");
            migrationBuilder.RenameIndex(name: "ix_appeal_bill_number_record", schema: "appeals", table: "bill_record", newName: "ix_bill_record_bill_number");
            migrationBuilder.RenameIndex(name: "ix_appeal_status", schema: "appeals", table: "bill_record", newName: "ix_bill_record_status");

            foreach (var table in Referencing)
            {
                migrationBuilder.RenameColumn(name: "appeal_id", schema: "appeals", table: table, newName: "record_id");
                migrationBuilder.Sql($"ALTER TABLE appeals.{table} RENAME CONSTRAINT fk_{table}_appeal_appeal_id TO fk_{table}_bill_record_record_id;");
            }

            migrationBuilder.RenameColumn(name: "appeal_id", schema: "appeals", table: "report", newName: "record_id");
            migrationBuilder.RenameIndex(name: "ix_endorsement_appeal_id_user_id", schema: "appeals", table: "endorsement", newName: "ix_endorsement_record_id_user_id");
            migrationBuilder.RenameIndex(name: "ix_flag_appeal_id_reporter_user_id", schema: "appeals", table: "flag", newName: "ix_flag_record_id_reporter_user_id");
            migrationBuilder.RenameIndex(name: "ix_flag_appeal_id_status", schema: "appeals", table: "flag", newName: "ix_flag_record_id_status");
            migrationBuilder.RenameIndex(name: "ix_reason_appeal_id_author_user_id", schema: "appeals", table: "reason", newName: "ix_reason_record_id_author_user_id");
            migrationBuilder.RenameIndex(name: "ix_reason_appeal_id_status", schema: "appeals", table: "reason", newName: "ix_reason_record_id_status");

            migrationBuilder.Sql("UPDATE appeals.report SET target_kind = 'record' WHERE target_kind = 'appeal';");
            migrationBuilder.Sql("UPDATE appeals.revision SET entity_kind = 'record' WHERE entity_kind = 'appeal';");
            migrationBuilder.Sql("UPDATE appeals.moderation_action SET target_kind = 'record' WHERE target_kind = 'appeal';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE appeals.moderation_action SET target_kind = 'appeal' WHERE target_kind = 'record';");
            migrationBuilder.Sql("UPDATE appeals.revision SET entity_kind = 'appeal' WHERE entity_kind = 'record';");
            migrationBuilder.Sql("UPDATE appeals.report SET target_kind = 'appeal' WHERE target_kind = 'record';");

            migrationBuilder.RenameIndex(name: "ix_reason_record_id_status", schema: "appeals", table: "reason", newName: "ix_reason_appeal_id_status");
            migrationBuilder.RenameIndex(name: "ix_reason_record_id_author_user_id", schema: "appeals", table: "reason", newName: "ix_reason_appeal_id_author_user_id");
            migrationBuilder.RenameIndex(name: "ix_flag_record_id_status", schema: "appeals", table: "flag", newName: "ix_flag_appeal_id_status");
            migrationBuilder.RenameIndex(name: "ix_flag_record_id_reporter_user_id", schema: "appeals", table: "flag", newName: "ix_flag_appeal_id_reporter_user_id");
            migrationBuilder.RenameIndex(name: "ix_endorsement_record_id_user_id", schema: "appeals", table: "endorsement", newName: "ix_endorsement_appeal_id_user_id");
            migrationBuilder.RenameColumn(name: "record_id", schema: "appeals", table: "report", newName: "appeal_id");

            foreach (var table in Referencing)
            {
                migrationBuilder.Sql($"ALTER TABLE appeals.{table} RENAME CONSTRAINT fk_{table}_bill_record_record_id TO fk_{table}_appeal_appeal_id;");
                migrationBuilder.RenameColumn(name: "record_id", schema: "appeals", table: table, newName: "appeal_id");
            }

            migrationBuilder.RenameIndex(name: "ix_bill_record_status", schema: "appeals", table: "bill_record", newName: "ix_appeal_status");
            migrationBuilder.RenameIndex(name: "ix_bill_record_bill_number", schema: "appeals", table: "bill_record", newName: "ix_appeal_bill_number_record");
            migrationBuilder.Sql("ALTER TABLE appeals.bill_record RENAME CONSTRAINT ck_bill_record_threshold_positive TO ck_appeal_threshold_positive;");
            migrationBuilder.Sql("ALTER TABLE appeals.bill_record RENAME CONSTRAINT pk_bill_record TO pk_appeal;");
            migrationBuilder.RenameTable(name: "bill_record", schema: "appeals", newName: "appeal", newSchema: "appeals");
        }
    }
}
