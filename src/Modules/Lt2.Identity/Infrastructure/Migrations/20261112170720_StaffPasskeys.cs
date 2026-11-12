using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StaffPasskeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "passkey_confirmed_at",
                schema: "identity",
                table: "session",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "passkey_id",
                schema: "identity",
                table: "session",
                type: "bytea",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "staff_passkey_id",
                schema: "identity",
                table: "profile",
                type: "bytea",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "role_activation",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    issued_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_activation", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_activation_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_role_activation_user_id_role",
                schema: "identity",
                table: "role_activation",
                columns: new[] { "user_id", "role" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_activation",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "passkey_confirmed_at",
                schema: "identity",
                table: "session");

            migrationBuilder.DropColumn(
                name: "passkey_id",
                schema: "identity",
                table: "session");

            migrationBuilder.DropColumn(
                name: "staff_passkey_id",
                schema: "identity",
                table: "profile");
        }
    }
}
