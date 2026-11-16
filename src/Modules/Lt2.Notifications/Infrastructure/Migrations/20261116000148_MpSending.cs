using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MpSending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_digest_delivery",
                schema: "notify",
                table: "digest_delivery");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                schema: "notify",
                table: "digest_run",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by",
                schema: "notify",
                table: "digest_run",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                schema: "notify",
                table: "digest_run",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "letter_version",
                schema: "notify",
                table: "digest_run",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sent_count",
                schema: "notify",
                table: "digest_run",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "statement",
                schema: "notify",
                table: "digest_run",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient",
                schema: "notify",
                table: "digest_delivery",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "official");

            migrationBuilder.AddColumn<long>(
                name: "seimas_person_id",
                schema: "notify",
                table: "digest_delivery",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddPrimaryKey(
                name: "pk_digest_delivery",
                schema: "notify",
                table: "digest_delivery",
                columns: new[] { "run_id", "mp_slug", "recipient" });

            migrationBuilder.CreateTable(
                name: "introduction_letter",
                schema: "notify",
                columns: table => new
                {
                    version = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    text = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    signer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    signer_role = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    written_by = table.Column<Guid>(type: "uuid", nullable: false),
                    written_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_introduction_letter", x => x.version);
                });

            migrationBuilder.CreateTable(
                name: "mp_email_pref",
                schema: "notify",
                columns: table => new
                {
                    seimas_person_id = table.Column<long>(type: "bigint", nullable: false),
                    frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    extra_address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    extra_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    token_generation = table.Column<int>(type: "integer", nullable: false),
                    extra_generation = table.Column<int>(type: "integer", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mp_email_pref", x => x.seimas_person_id);
                });

            migrationBuilder.CreateTable(
                name: "mp_mail_token",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seimas_person_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mp_mail_token", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suppression",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: true),
                    statement = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppression", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_digest_run_status",
                schema: "notify",
                table: "digest_run",
                column: "status",
                filter: "status = 'waiting'");

            migrationBuilder.CreateIndex(
                name: "ix_digest_delivery_seimas_person_id_status",
                schema: "notify",
                table: "digest_delivery",
                columns: new[] { "seimas_person_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_mp_mail_token_seimas_person_id_kind",
                schema: "notify",
                table: "mp_mail_token",
                columns: new[] { "seimas_person_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_mp_mail_token_token_hash",
                schema: "notify",
                table: "mp_mail_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suppression_address_hash",
                schema: "notify",
                table: "suppression",
                column: "address_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "introduction_letter",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "mp_email_pref",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "mp_mail_token",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "suppression",
                schema: "notify");

            migrationBuilder.DropIndex(
                name: "ix_digest_run_status",
                schema: "notify",
                table: "digest_run");

            migrationBuilder.DropPrimaryKey(
                name: "pk_digest_delivery",
                schema: "notify",
                table: "digest_delivery");

            migrationBuilder.DropIndex(
                name: "ix_digest_delivery_seimas_person_id_status",
                schema: "notify",
                table: "digest_delivery");

            migrationBuilder.DropColumn(
                name: "decided_at",
                schema: "notify",
                table: "digest_run");

            migrationBuilder.DropColumn(
                name: "decided_by",
                schema: "notify",
                table: "digest_run");

            migrationBuilder.DropColumn(
                name: "expires_at",
                schema: "notify",
                table: "digest_run");

            migrationBuilder.DropColumn(
                name: "letter_version",
                schema: "notify",
                table: "digest_run");

            migrationBuilder.DropColumn(
                name: "sent_count",
                schema: "notify",
                table: "digest_run");

            migrationBuilder.DropColumn(
                name: "statement",
                schema: "notify",
                table: "digest_run");

            migrationBuilder.DropColumn(
                name: "recipient",
                schema: "notify",
                table: "digest_delivery");

            migrationBuilder.DropColumn(
                name: "seimas_person_id",
                schema: "notify",
                table: "digest_delivery");

            migrationBuilder.AddPrimaryKey(
                name: "pk_digest_delivery",
                schema: "notify",
                table: "digest_delivery",
                columns: new[] { "run_id", "mp_slug" });
        }
    }
}
