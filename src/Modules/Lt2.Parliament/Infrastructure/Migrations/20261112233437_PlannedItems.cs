using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Parliament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlannedItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agenda_snapshot",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<byte[]>(type: "bytea", nullable: false),
                    synthetic = table.Column<bool>(type: "boolean", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    failure = table.Column<string>(type: "text", nullable: true),
                    rows = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agenda_snapshot", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "planned_item",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bill_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    planned_date = table.Column<DateOnly>(type: "date", nullable: false),
                    stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    first_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    gone_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planned_item", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_snapshot_fetched_at",
                schema: "parliament",
                table: "agenda_snapshot",
                column: "fetched_at");

            migrationBuilder.CreateIndex(
                name: "ix_planned_item_bill_number_gone_at",
                schema: "parliament",
                table: "planned_item",
                columns: new[] { "bill_number", "gone_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agenda_snapshot",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "planned_item",
                schema: "parliament");
        }
    }
}
