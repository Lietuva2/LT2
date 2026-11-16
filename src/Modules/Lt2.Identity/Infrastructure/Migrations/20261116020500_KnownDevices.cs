using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KnownDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "known_device_id",
                schema: "identity",
                table: "sign_in_code",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "known_device",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_known_device", x => x.id);
                    table.ForeignKey(
                        name: "fk_known_device_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sign_in_code_known_device_id",
                schema: "identity",
                table: "sign_in_code",
                column: "known_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_known_device_last_seen_at",
                schema: "identity",
                table: "known_device",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "ix_known_device_user_id_identifier_hash",
                schema: "identity",
                table: "known_device",
                columns: new[] { "user_id", "identifier_hash" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_sign_in_code_known_devices_known_device_id",
                schema: "identity",
                table: "sign_in_code",
                column: "known_device_id",
                principalSchema: "identity",
                principalTable: "known_device",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sign_in_code_known_devices_known_device_id",
                schema: "identity",
                table: "sign_in_code");

            migrationBuilder.DropTable(
                name: "known_device",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "ix_sign_in_code_known_device_id",
                schema: "identity",
                table: "sign_in_code");

            migrationBuilder.DropColumn(
                name: "known_device_id",
                schema: "identity",
                table: "sign_in_code");
        }
    }
}
