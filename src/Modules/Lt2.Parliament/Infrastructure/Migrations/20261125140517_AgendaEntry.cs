using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Parliament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgendaEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agenda_entry",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    planned_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entered_by = table.Column<Guid>(type: "uuid", nullable: false),
                    rows = table.Column<int>(type: "integer", nullable: false),
                    added = table.Column<int>(type: "integer", nullable: false),
                    changed = table.Column<int>(type: "integer", nullable: false),
                    removed = table.Column<int>(type: "integer", nullable: false),
                    created_bills = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agenda_entry", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_entry_planned_date_entered_at",
                schema: "parliament",
                table: "agenda_entry",
                columns: new[] { "planned_date", "entered_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agenda_entry",
                schema: "parliament");
        }
    }
}
