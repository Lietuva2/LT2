using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PublicProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "about",
                schema: "identity",
                table: "profile",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "about_changed_at",
                schema: "identity",
                table: "profile",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "about_hidden_at",
                schema: "identity",
                table: "profile",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "about_hidden_statement",
                schema: "identity",
                table: "profile",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_profile_id",
                schema: "identity",
                table: "profile",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "public_since",
                schema: "identity",
                table: "profile",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "profile_report",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ground = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profile_report", x => x.id);
                    table.ForeignKey(
                        name: "fk_profile_report_user_reporter_user_id",
                        column: x => x.reporter_user_id,
                        principalSchema: "identity",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_profile_report_user_target_user_id",
                        column: x => x.target_user_id,
                        principalSchema: "identity",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "retired_profile_id",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retired_profile_id", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_profile_public_profile_id",
                schema: "identity",
                table: "profile",
                column: "public_profile_id",
                unique: true,
                filter: "public_profile_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_profile_report_reporter_user_id_target_user_id",
                schema: "identity",
                table: "profile_report",
                columns: new[] { "reporter_user_id", "target_user_id" },
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "ix_profile_report_target_user_id_status",
                schema: "identity",
                table: "profile_report",
                columns: new[] { "target_user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profile_report",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "retired_profile_id",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "ix_profile_public_profile_id",
                schema: "identity",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "about",
                schema: "identity",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "about_changed_at",
                schema: "identity",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "about_hidden_at",
                schema: "identity",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "about_hidden_statement",
                schema: "identity",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "public_profile_id",
                schema: "identity",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "public_since",
                schema: "identity",
                table: "profile");
        }
    }
}
