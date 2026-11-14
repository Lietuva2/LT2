using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DescriptionSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "description_at",
                schema: "appeals",
                table: "appeal",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description_url",
                schema: "appeals",
                table: "appeal",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Design 8.13: the day a description was made, for the ones already there: its latest edit in the history, or
            // the record's last change when the history has none.
            migrationBuilder.Sql(
                """
                UPDATE appeals.appeal a
                SET description_at = COALESCE(
                    (SELECT max(r.changed_at) FROM appeals.revision r
                     WHERE r.entity_kind = 'appeal' AND r.entity_id = a.id AND r.field = 'description'),
                    a.changed_at)
                WHERE a.description <> '';
                """);

            migrationBuilder.CreateTable(
                name: "summary_attempt",
                schema: "appeals",
                columns: table => new
                {
                    bill_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tried_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    asked_provider = table.Column<bool>(type: "boolean", nullable: false),
                    retry_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_summary_attempt", x => x.bill_number);
                });

            migrationBuilder.CreateIndex(
                name: "ix_summary_attempt_tried_at",
                schema: "appeals",
                table: "summary_attempt",
                column: "tried_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "summary_attempt",
                schema: "appeals");

            migrationBuilder.DropColumn(
                name: "description_at",
                schema: "appeals",
                table: "appeal");

            migrationBuilder.DropColumn(
                name: "description_url",
                schema: "appeals",
                table: "appeal");
        }
    }
}
