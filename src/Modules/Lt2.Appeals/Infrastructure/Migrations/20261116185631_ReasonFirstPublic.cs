using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReasonFirstPublic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_public_at",
                schema: "appeals",
                table: "reason",
                type: "timestamp with time zone",
                nullable: true);

            // An argument already published became public when it was written, as far as anyone can tell now.
            migrationBuilder.Sql("UPDATE appeals.reason SET first_public_at = created_at WHERE status IN ('published', 'hidden', 'merged')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "first_public_at",
                schema: "appeals",
                table: "reason");
        }
    }
}
