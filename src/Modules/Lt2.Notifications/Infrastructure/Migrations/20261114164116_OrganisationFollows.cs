using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrganisationFollows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "statements",
                schema: "notify",
                table: "notice_setting",
                type: "boolean",
                nullable: false,

                // Rows saved before the switch existed: "no row means all on" (design 8.11.2), so the new switch starts on.
                defaultValue: true);

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "notify",
                table: "follow",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "statements",
                schema: "notify",
                table: "notice_setting");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "notify",
                table: "follow",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);
        }
    }
}
