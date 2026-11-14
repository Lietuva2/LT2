using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Follows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bill_event",
                schema: "notify",
                columns: table => new
                {
                    bill_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    first_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bill_event", x => new { x.bill_number, x.kind, x.key });
                });

            migrationBuilder.CreateTable(
                name: "follow",
                schema: "notify",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follow", x => new { x.user_id, x.kind, x.key });
                });

            migrationBuilder.CreateTable(
                name: "notice_setting",
                schema: "notify",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    planned = table.Column<bool>(type: "boolean", nullable: false),
                    voted = table.Column<bool>(type: "boolean", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notice_setting", x => x.user_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bill_event_outcome",
                schema: "notify",
                table: "bill_event",
                column: "outcome",
                filter: "outcome = 'waiting'");

            migrationBuilder.CreateIndex(
                name: "ix_follow_kind_key",
                schema: "notify",
                table: "follow",
                columns: new[] { "kind", "key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bill_event",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "follow",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "notice_setting",
                schema: "notify");
        }
    }
}
