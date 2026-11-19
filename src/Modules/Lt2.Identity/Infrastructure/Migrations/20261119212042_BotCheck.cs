using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BotCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bot_check_use",
                schema: "identity",
                columns: table => new
                {
                    signature = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bot_check_use", x => x.signature);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bot_check_use_expires_at",
                schema: "identity",
                table: "bot_check_use",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_check_use",
                schema: "identity");
        }
    }
}
