using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DigestReasonCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "against_reason_count",
                schema: "notify",
                table: "digest_item",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "for_reason_count",
                schema: "notify",
                table: "digest_item",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "against_reason_count",
                schema: "notify",
                table: "digest_item");

            migrationBuilder.DropColumn(
                name: "for_reason_count",
                schema: "notify",
                table: "digest_item");
        }
    }
}
