using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClosingCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "closed_against",
                schema: "appeals",
                table: "appeal",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "closed_against_older_than30days",
                schema: "appeals",
                table: "appeal",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "closed_for",
                schema: "appeals",
                table: "appeal",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "closed_for_older_than30days",
                schema: "appeals",
                table: "appeal",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "closed_against",
                schema: "appeals",
                table: "appeal");

            migrationBuilder.DropColumn(
                name: "closed_against_older_than30days",
                schema: "appeals",
                table: "appeal");

            migrationBuilder.DropColumn(
                name: "closed_for",
                schema: "appeals",
                table: "appeal");

            migrationBuilder.DropColumn(
                name: "closed_for_older_than30days",
                schema: "appeals",
                table: "appeal");
        }
    }
}
