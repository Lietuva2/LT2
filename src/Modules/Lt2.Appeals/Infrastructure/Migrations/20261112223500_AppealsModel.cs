using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppealsModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "appeals");

            migrationBuilder.CreateTable(
                name: "appeal",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bill_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    initiator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closing_vote_id = table.Column<long>(type: "bigint", nullable: true),
                    sending = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    threshold = table.Column<int>(type: "integer", nullable: false),
                    threshold_set_by = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    threshold_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    first_summarised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    return_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    screening_flags = table.Column<string[]>(type: "text[]", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appeal", x => x.id);
                    table.CheckConstraint("ck_appeal_threshold_positive", "threshold > 0");
                });

            migrationBuilder.CreateTable(
                name: "moderation_action",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    moderator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    statement = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_action", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appeal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ground = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "revision",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    old_value = table.Column<string>(type: "text", nullable: true),
                    new_value = table.Column<string>(type: "text", nullable: true),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revision", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "daily_stat",
                schema: "appeals",
                columns: table => new
                {
                    appeal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    for_count = table.Column<int>(type: "integer", nullable: false),
                    against_count = table.Column<int>(type: "integer", nullable: false),
                    for_older_than30days = table.Column<int>(type: "integer", nullable: false),
                    against_older_than30days = table.Column<int>(type: "integer", nullable: false),
                    new_account_share = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_stat", x => new { x.appeal_id, x.day });
                    table.ForeignKey(
                        name: "fk_daily_stat_appeal_appeal_id",
                        column: x => x.appeal_id,
                        principalSchema: "appeals",
                        principalTable: "appeal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "position",
                schema: "appeals",
                columns: table => new
                {
                    appeal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    member_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position", x => new { x.appeal_id, x.user_id });
                    table.CheckConstraint("ck_position_side", "side IN ('for', 'against')");
                    table.ForeignKey(
                        name: "fk_position_appeal_appeal_id",
                        column: x => x.appeal_id,
                        principalSchema: "appeals",
                        principalTable: "appeal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reason",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appeal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    merged_into_id = table.Column<Guid>(type: "uuid", nullable: true),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    screening_flags = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reason", x => x.id);
                    table.CheckConstraint("ck_reason_side", "side IN ('for', 'against')");
                    table.ForeignKey(
                        name: "fk_reason_appeal_appeal_id",
                        column: x => x.appeal_id,
                        principalSchema: "appeals",
                        principalTable: "appeal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reason_reason_merged_into_id",
                        column: x => x.merged_into_id,
                        principalSchema: "appeals",
                        principalTable: "reason",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "endorsement",
                schema: "appeals",
                columns: table => new
                {
                    reason_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appeal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_endorsement", x => new { x.reason_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_endorsement_appeal_appeal_id",
                        column: x => x.appeal_id,
                        principalSchema: "appeals",
                        principalTable: "appeal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_endorsement_reason_reason_id",
                        column: x => x.reason_id,
                        principalSchema: "appeals",
                        principalTable: "reason",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appeal_bill_number",
                schema: "appeals",
                table: "appeal",
                column: "bill_number",
                unique: true,
                filter: "status <> 'rejected'");

            migrationBuilder.CreateIndex(
                name: "ix_appeal_status",
                schema: "appeals",
                table: "appeal",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_endorsement_appeal_id_user_id",
                schema: "appeals",
                table: "endorsement",
                columns: new[] { "appeal_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_action_target_kind_target_id",
                schema: "appeals",
                table: "moderation_action",
                columns: new[] { "target_kind", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_position_user_id",
                schema: "appeals",
                table: "position",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_reason_appeal_id_author_user_id",
                schema: "appeals",
                table: "reason",
                columns: new[] { "appeal_id", "author_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_reason_appeal_id_status",
                schema: "appeals",
                table: "reason",
                columns: new[] { "appeal_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_reason_merged_into_id",
                schema: "appeals",
                table: "reason",
                column: "merged_into_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_target_id_reporter_user_id",
                schema: "appeals",
                table: "report",
                columns: new[] { "target_id", "reporter_user_id" },
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "ix_report_target_kind_target_id_status",
                schema: "appeals",
                table: "report",
                columns: new[] { "target_kind", "target_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_revision_entity_kind_entity_id",
                schema: "appeals",
                table: "revision",
                columns: new[] { "entity_kind", "entity_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_stat",
                schema: "appeals");

            migrationBuilder.DropTable(
                name: "endorsement",
                schema: "appeals");

            migrationBuilder.DropTable(
                name: "moderation_action",
                schema: "appeals");

            migrationBuilder.DropTable(
                name: "position",
                schema: "appeals");

            migrationBuilder.DropTable(
                name: "report",
                schema: "appeals");

            migrationBuilder.DropTable(
                name: "revision",
                schema: "appeals");

            migrationBuilder.DropTable(
                name: "reason",
                schema: "appeals");

            migrationBuilder.DropTable(
                name: "appeal",
                schema: "appeals");
        }
    }
}
