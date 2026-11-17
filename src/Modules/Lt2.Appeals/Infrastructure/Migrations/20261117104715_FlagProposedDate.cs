using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FlagProposedDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "proposed_date",
                schema: "appeals",
                table: "flag",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "proposed_date",
                schema: "appeals",
                table: "flag");
        }
    }
}
