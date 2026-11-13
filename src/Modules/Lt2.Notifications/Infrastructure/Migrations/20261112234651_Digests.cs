using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Digests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "digest_run",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    run_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sitting_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    mp_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_digest_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "digest_delivery",
                schema: "notify",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mp_slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    party_numbers = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_digest_delivery", x => new { x.run_id, x.mp_slug });
                    table.ForeignKey(
                        name: "fk_digest_delivery_digest_run_run_id",
                        column: x => x.run_id,
                        principalSchema: "notify",
                        principalTable: "digest_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "digest_item",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    appeal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bill_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    bill_title = table.Column<string>(type: "text", nullable: false),
                    stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    planned_date = table.Column<DateOnly>(type: "date", nullable: true),
                    for_count = table.Column<int>(type: "integer", nullable: false),
                    for_older_than30days = table.Column<int>(type: "integer", nullable: false),
                    against_count = table.Column<int>(type: "integer", nullable: false),
                    against_older_than30days = table.Column<int>(type: "integer", nullable: false),
                    surge_share_percent = table.Column<int>(type: "integer", nullable: true),
                    surge_window_hours = table.Column<int>(type: "integer", nullable: true),
                    reasons = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_digest_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_digest_item_digest_run_run_id",
                        column: x => x.run_id,
                        principalSchema: "notify",
                        principalTable: "digest_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_digest_item_appeal_id",
                schema: "notify",
                table: "digest_item",
                column: "appeal_id");

            migrationBuilder.CreateIndex(
                name: "ix_digest_item_run_id",
                schema: "notify",
                table: "digest_item",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_digest_run_run_date",
                schema: "notify",
                table: "digest_run",
                column: "run_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "digest_delivery",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "digest_item",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "digest_run",
                schema: "notify");
        }
    }
}
