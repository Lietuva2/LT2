using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Organisations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "appeal_id",
                schema: "appeals",
                table: "report",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "organisation",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(68)", maxLength: 68, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    website = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    legal_code = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    declared_lobbyist = table.Column<bool>(type: "boolean", nullable: false),
                    registered_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    confirmed_by = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    confirmed_domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmation_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    domain_barred = table.Column<bool>(type: "boolean", nullable: false),
                    blocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    blocked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    block_statement = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    screening_flags = table.Column<string[]>(type: "text[]", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organisation", x => x.id);
                    table.CheckConstraint("ck_organisation_confirmed_by", "confirmed_by IS NULL OR confirmed_by IN ('domain', 'moderator')");
                    table.CheckConstraint("ck_organisation_status", "status IN ('active', 'blocked', 'closed')");
                });

            migrationBuilder.CreateTable(
                name: "organisation_code",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organisation_code", x => x.id);
                    table.ForeignKey(
                        name: "fk_organisation_code_organisation_organisation_id",
                        column: x => x.organisation_id,
                        principalSchema: "appeals",
                        principalTable: "organisation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organisation_invite",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organisation_invite", x => x.id);
                    table.ForeignKey(
                        name: "fk_organisation_invite_organisation_organisation_id",
                        column: x => x.organisation_id,
                        principalSchema: "appeals",
                        principalTable: "organisation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organisation_representative",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organisation_representative", x => x.id);
                    table.ForeignKey(
                        name: "fk_organisation_representative_organisation_organisation_id",
                        column: x => x.organisation_id,
                        principalSchema: "appeals",
                        principalTable: "organisation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organisation_statement",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bill_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    stance = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    text = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    goal = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    screening_flags = table.Column<string[]>(type: "text[]", nullable: false),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    written_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organisation_statement", x => x.id);
                    table.CheckConstraint("ck_organisation_statement_goal", "goal IS NULL OR (goal BETWEEN 1 AND 1000000 AND stance <> 'remarks')");
                    table.CheckConstraint("ck_organisation_statement_stance", "stance IN ('for', 'against', 'remarks')");
                    table.ForeignKey(
                        name: "fk_organisation_statement_organisation_organisation_id",
                        column: x => x.organisation_id,
                        principalSchema: "appeals",
                        principalTable: "organisation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_organisation_name_key_active",
                schema: "appeals",
                table: "organisation",
                column: "name_key",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_organisation_registered_by_user_id",
                schema: "appeals",
                table: "organisation",
                column: "registered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_organisation_slug",
                schema: "appeals",
                table: "organisation",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organisation_code_organisation_id_created_at",
                schema: "appeals",
                table: "organisation_code",
                columns: new[] { "organisation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_organisation_invite_organisation_id",
                schema: "appeals",
                table: "organisation_invite",
                column: "organisation_id");

            migrationBuilder.CreateIndex(
                name: "ix_organisation_invite_token_hash",
                schema: "appeals",
                table: "organisation_invite",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organisation_representative_current",
                schema: "appeals",
                table: "organisation_representative",
                columns: new[] { "organisation_id", "user_id" },
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_organisation_representative_user_id",
                schema: "appeals",
                table: "organisation_representative",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_organisation_statement_bill_number_status",
                schema: "appeals",
                table: "organisation_statement",
                columns: new[] { "bill_number", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_organisation_statement_one_per_bill",
                schema: "appeals",
                table: "organisation_statement",
                columns: new[] { "organisation_id", "bill_number" },
                unique: true,
                filter: "status IN ('pending', 'published', 'hidden')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organisation_code",
                schema: "appeals");

            migrationBuilder.DropTable(
                name: "organisation_invite",
                schema: "appeals");

            migrationBuilder.DropTable(
                name: "organisation_representative",
                schema: "appeals");

            migrationBuilder.DropTable(
                name: "organisation_statement",
                schema: "appeals");

            migrationBuilder.DropTable(
                name: "organisation",
                schema: "appeals");

            migrationBuilder.AlterColumn<Guid>(
                name: "appeal_id",
                schema: "appeals",
                table: "report",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
