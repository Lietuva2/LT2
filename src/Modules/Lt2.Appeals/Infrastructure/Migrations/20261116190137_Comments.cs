using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Comments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comment",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bill_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    speaks_as = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    seimas_person_id = table.Column<long>(type: "bigint", nullable: true),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    text = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    hidden_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comment", x => x.id);
                    table.CheckConstraint("ck_comment_status", "status IN ('published', 'hidden')");
                    table.CheckConstraint("ck_comment_target_kind", "target_kind IN ('reason', 'statement', 'response')");
                    table.CheckConstraint("ck_comment_voice", "(speaks_as = 'person' AND organisation_id IS NULL AND seimas_person_id IS NULL) OR (speaks_as = 'organisation' AND organisation_id IS NOT NULL AND target_kind = 'statement') OR (speaks_as = 'mp' AND seimas_person_id IS NOT NULL AND target_kind = 'response')");
                });

            migrationBuilder.CreateTable(
                name: "comment_agreement",
                schema: "appeals",
                columns: table => new
                {
                    comment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comment_agreement", x => new { x.comment_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_comment_agreement_comment_comment_id",
                        column: x => x.comment_id,
                        principalSchema: "appeals",
                        principalTable: "comment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_comment_author_user_id",
                schema: "appeals",
                table: "comment",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_comment_bill_number",
                schema: "appeals",
                table: "comment",
                column: "bill_number");

            migrationBuilder.CreateIndex(
                name: "ix_comment_created_at",
                schema: "appeals",
                table: "comment",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_comment_target_kind_target_id_created_at",
                schema: "appeals",
                table: "comment",
                columns: new[] { "target_kind", "target_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_comment_agreement_user_id",
                schema: "appeals",
                table: "comment_agreement",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comment_agreement",
                schema: "appeals");

            migrationBuilder.DropTable(
                name: "comment",
                schema: "appeals");
        }
    }
}
