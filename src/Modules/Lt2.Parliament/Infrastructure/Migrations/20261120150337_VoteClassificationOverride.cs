using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Parliament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VoteClassificationOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind_override",
                schema: "parliament",
                table: "vote",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "overridden_at",
                schema: "parliament",
                table: "vote",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "overridden_by",
                schema: "parliament",
                table: "vote",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "override_statement",
                schema: "parliament",
                table: "vote",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stage_override",
                schema: "parliament",
                table: "vote",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind_override",
                schema: "parliament",
                table: "vote");

            migrationBuilder.DropColumn(
                name: "overridden_at",
                schema: "parliament",
                table: "vote");

            migrationBuilder.DropColumn(
                name: "overridden_by",
                schema: "parliament",
                table: "vote");

            migrationBuilder.DropColumn(
                name: "override_statement",
                schema: "parliament",
                table: "vote");

            migrationBuilder.DropColumn(
                name: "stage_override",
                schema: "parliament",
                table: "vote");
        }
    }
}
