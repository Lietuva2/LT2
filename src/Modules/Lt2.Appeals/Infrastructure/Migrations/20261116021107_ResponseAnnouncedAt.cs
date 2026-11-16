using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ResponseAnnouncedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "announced_at",
                schema: "appeals",
                table: "mp_response",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Design 8.20.1: a response written before this knew only its first publication; that is its announced stance.
            migrationBuilder.Sql("UPDATE appeals.mp_response SET announced_at = published_at");

            migrationBuilder.CreateIndex(
                name: "ix_mp_response_announced_at",
                schema: "appeals",
                table: "mp_response",
                column: "announced_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mp_response_announced_at",
                schema: "appeals",
                table: "mp_response");

            migrationBuilder.DropColumn(
                name: "announced_at",
                schema: "appeals",
                table: "mp_response");
        }
    }
}
