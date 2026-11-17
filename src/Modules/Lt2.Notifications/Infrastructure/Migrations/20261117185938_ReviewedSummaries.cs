using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Notifications.Infrastructure.Migrations
{
    /// <summary>
    /// Design 8.19.2 (owner, 2026-11-17): summaries are prepared a day ahead for review and go at their time; nobody approves a
    /// send. The waiting and expired runs of the approval mode go (nothing is launched, no data to keep); a run keeps when it
    /// was due and when it went, an approval mark and a decline; the arguments chosen for a prepared run are their own table.
    /// </summary>
    public partial class ReviewedSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM notify.digest_run WHERE status IN ('waiting', 'expired')");
            migrationBuilder.Sql("DELETE FROM notify.digest_delivery WHERE status IN ('waiting', 'not-sent')");

            migrationBuilder.DropIndex(name: "ix_digest_run_status", schema: "notify", table: "digest_run");
            migrationBuilder.DropColumn(name: "expires_at", schema: "notify", table: "digest_run");
            migrationBuilder.RenameColumn(name: "decided_by", schema: "notify", table: "digest_run", newName: "cancelled_by");
            migrationBuilder.RenameColumn(name: "decided_at", schema: "notify", table: "digest_run", newName: "sent_at");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "due_at", schema: "notify", table: "digest_run", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at", schema: "notify", table: "digest_run", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at", schema: "notify", table: "digest_run", type: "timestamp with time zone", nullable: true);

            // What went before: due and gone when it was made; a declined one was declined then.
            migrationBuilder.Sql(
                """
                UPDATE notify.digest_run SET due_at = created_at, cancelled_at = sent_at, sent_at = NULL WHERE status = 'cancelled';
                UPDATE notify.digest_run SET due_at = created_at, sent_at = coalesce(sent_at, created_at), cancelled_by = NULL WHERE status <> 'cancelled';
                UPDATE notify.digest_run SET approved_at = created_at WHERE approved_by IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "digest_choice",
                schema: "notify",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    place = table.Column<int>(type: "integer", nullable: false),
                    replacement = table.Column<bool>(type: "boolean", nullable: false),
                    left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_digest_choice", x => new { x.run_id, x.reason_id });
                    table.ForeignKey(
                        name: "fk_digest_choice_digest_run_run_id",
                        column: x => x.run_id,
                        principalSchema: "notify",
                        principalTable: "digest_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_digest_run_due_at",
                schema: "notify",
                table: "digest_run",
                column: "due_at",
                filter: "status = 'prepared'");

            migrationBuilder.CreateIndex(
                name: "ix_digest_choice_run_id_record_id",
                schema: "notify",
                table: "digest_choice",
                columns: new[] { "run_id", "record_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM notify.digest_run WHERE status IN ('prepared', 'dropped')");
            migrationBuilder.DropTable(name: "digest_choice", schema: "notify");
            migrationBuilder.DropIndex(name: "ix_digest_run_due_at", schema: "notify", table: "digest_run");
            migrationBuilder.Sql("UPDATE notify.digest_run SET sent_at = cancelled_at WHERE status = 'cancelled'");
            migrationBuilder.DropColumn(name: "due_at", schema: "notify", table: "digest_run");
            migrationBuilder.DropColumn(name: "approved_at", schema: "notify", table: "digest_run");
            migrationBuilder.DropColumn(name: "cancelled_at", schema: "notify", table: "digest_run");
            migrationBuilder.RenameColumn(name: "sent_at", schema: "notify", table: "digest_run", newName: "decided_at");
            migrationBuilder.RenameColumn(name: "cancelled_by", schema: "notify", table: "digest_run", newName: "decided_by");
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at", schema: "notify", table: "digest_run", type: "timestamp with time zone", nullable: true);
            migrationBuilder.CreateIndex(
                name: "ix_digest_run_status",
                schema: "notify",
                table: "digest_run",
                column: "status",
                filter: "status = 'waiting'");
        }
    }
}
