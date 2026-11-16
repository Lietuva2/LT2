using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RhythmPassAndEventIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rhythm_pass",
                schema: "notify",
                columns: table => new
                {
                    pass = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    made_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rhythm_pass", x => new { x.pass, x.period });
                });

            migrationBuilder.CreateIndex(
                name: "ix_bill_event_kind_first_seen",
                schema: "notify",
                table: "bill_event",
                columns: new[] { "kind", "first_seen" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rhythm_pass",
                schema: "notify");

            migrationBuilder.DropIndex(
                name: "ix_bill_event_kind_first_seen",
                schema: "notify",
                table: "bill_event");
        }
    }
}
