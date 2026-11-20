using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrganisationKeptOutOfSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "kept_out_by_user_id",
                schema: "appeals",
                table: "organisation",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "kept_out_of_summaries_at",
                schema: "appeals",
                table: "organisation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kept_out_statement",
                schema: "appeals",
                table: "organisation",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kept_out_by_user_id",
                schema: "appeals",
                table: "organisation");

            migrationBuilder.DropColumn(
                name: "kept_out_of_summaries_at",
                schema: "appeals",
                table: "organisation");

            migrationBuilder.DropColumn(
                name: "kept_out_statement",
                schema: "appeals",
                table: "organisation");
        }
    }
}
