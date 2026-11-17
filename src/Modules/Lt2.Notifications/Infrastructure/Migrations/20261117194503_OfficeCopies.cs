using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OfficeCopies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_digest_delivery",
                schema: "notify",
                table: "digest_delivery");

            migrationBuilder.AddColumn<Guid>(
                name: "office_user_id",
                schema: "notify",
                table: "digest_delivery",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "pk_digest_delivery",
                schema: "notify",
                table: "digest_delivery",
                columns: new[] { "run_id", "mp_slug", "recipient", "office_user_id" });

            migrationBuilder.CreateTable(
                name: "office_copy",
                schema: "notify",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seimas_person_id = table.Column<long>(type: "bigint", nullable: false),
                    frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    generation = table.Column<int>(type: "integer", nullable: false),
                    stop_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_office_copy", x => new { x.user_id, x.seimas_person_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_digest_delivery_office_user_id",
                schema: "notify",
                table: "digest_delivery",
                column: "office_user_id",
                filter: "recipient = 'office'");

            migrationBuilder.CreateIndex(
                name: "ix_office_copy_seimas_person_id",
                schema: "notify",
                table: "office_copy",
                column: "seimas_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_office_copy_stop_token_hash",
                schema: "notify",
                table: "office_copy",
                column: "stop_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "office_copy",
                schema: "notify");

            migrationBuilder.DropPrimaryKey(
                name: "pk_digest_delivery",
                schema: "notify",
                table: "digest_delivery");

            migrationBuilder.DropIndex(
                name: "ix_digest_delivery_office_user_id",
                schema: "notify",
                table: "digest_delivery");

            migrationBuilder.DropColumn(
                name: "office_user_id",
                schema: "notify",
                table: "digest_delivery");

            migrationBuilder.AddPrimaryKey(
                name: "pk_digest_delivery",
                schema: "notify",
                table: "digest_delivery",
                columns: new[] { "run_id", "mp_slug", "recipient" });
        }
    }
}
