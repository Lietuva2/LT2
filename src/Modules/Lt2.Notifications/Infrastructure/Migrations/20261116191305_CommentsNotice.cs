using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CommentsNotice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "comments",
                schema: "notify",
                table: "notice_setting",
                type: "boolean",
                nullable: false,
                defaultValue: true); // design 8.23.9: existing rows start on
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "comments",
                schema: "notify",
                table: "notice_setting");
        }
    }
}
