using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppealRequestsAndModeratorAppeals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "initiator_user_id",
                schema: "appeals",
                table: "appeal",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "description_source",
                schema: "appeals",
                table: "appeal",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "initiator");

            migrationBuilder.CreateTable(
                name: "ai_draft_request",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    moderator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bill_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_draft_request", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "appeal_request",
                schema: "appeals",
                columns: table => new
                {
                    bill_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appeal_request", x => new { x.bill_number, x.user_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_draft_request_moderator_user_id_requested_at",
                schema: "appeals",
                table: "ai_draft_request",
                columns: new[] { "moderator_user_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appeal_request_user_id",
                schema: "appeals",
                table: "appeal_request",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_draft_request",
                schema: "appeals");

            migrationBuilder.DropTable(
                name: "appeal_request",
                schema: "appeals");

            migrationBuilder.DropColumn(
                name: "description_source",
                schema: "appeals",
                table: "appeal");

            migrationBuilder.AlterColumn<Guid>(
                name: "initiator_user_id",
                schema: "appeals",
                table: "appeal",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
