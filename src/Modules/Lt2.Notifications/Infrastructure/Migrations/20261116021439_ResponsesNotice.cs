using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ResponsesNotice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "responses",
                schema: "notify",
                table: "notice_setting",
                type: "boolean",
                nullable: false,

                // Design 8.20.1: the fourth switch is on at first, for those who changed the others too.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "responses",
                schema: "notify",
                table: "notice_setting");
        }
    }
}
