using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MpLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "politician_link",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seimas_person_id = table.Column<long>(type: "bigint", nullable: false),
                    part = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    verified_by = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_politician_link", x => x.id);
                    table.ForeignKey(
                        name: "fk_politician_link_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "politician_token",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    seimas_person_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_politician_token", x => x.id);
                    table.ForeignKey(
                        name: "fk_politician_token_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_politician_link_account_open",
                schema: "identity",
                table: "politician_link",
                column: "user_id",
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_politician_link_mp_open",
                schema: "identity",
                table: "politician_link",
                column: "seimas_person_id",
                unique: true,
                filter: "ended_at IS NULL AND part = 'mp'");

            migrationBuilder.CreateIndex(
                name: "ix_politician_link_seimas_person_id_part",
                schema: "identity",
                table: "politician_link",
                columns: new[] { "seimas_person_id", "part" });

            migrationBuilder.CreateIndex(
                name: "ix_politician_token_seimas_person_id_kind_created_at",
                schema: "identity",
                table: "politician_token",
                columns: new[] { "seimas_person_id", "kind", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_politician_token_token_hash",
                schema: "identity",
                table: "politician_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_politician_token_user_id",
                schema: "identity",
                table: "politician_token",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "politician_link",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "politician_token",
                schema: "identity");
        }
    }
}
