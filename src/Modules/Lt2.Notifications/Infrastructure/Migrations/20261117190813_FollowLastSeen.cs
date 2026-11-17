using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FollowLastSeen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_seen_at",
                schema: "notify",
                table: "follow",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_seen_at",
                schema: "notify",
                table: "follow");
        }
    }
}
