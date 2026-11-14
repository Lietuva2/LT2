using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrganisationFollows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_public_at",
                schema: "appeals",
                table: "organisation_statement",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_organisation_statement_organisation_id_first_public_at",
                schema: "appeals",
                table: "organisation_statement",
                columns: new[] { "organisation_id", "first_public_at" });

            // Statements shown or hidden before this column existed were public from when they were published (design
            // 8.14.14, stage B); one that waits has never been public.
            migrationBuilder.Sql(
                "UPDATE appeals.organisation_statement SET first_public_at = published_at WHERE status IN ('published', 'hidden')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_organisation_statement_organisation_id_first_public_at",
                schema: "appeals",
                table: "organisation_statement");

            migrationBuilder.DropColumn(
                name: "first_public_at",
                schema: "appeals",
                table: "organisation_statement");
        }
    }
}
