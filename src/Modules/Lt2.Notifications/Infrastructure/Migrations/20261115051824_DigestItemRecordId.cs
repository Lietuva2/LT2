using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DigestItemRecordId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "appeal_id",
                schema: "notify",
                table: "digest_item",
                newName: "record_id");

            migrationBuilder.RenameIndex(
                name: "ix_digest_item_appeal_id",
                schema: "notify",
                table: "digest_item",
                newName: "ix_digest_item_record_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "record_id",
                schema: "notify",
                table: "digest_item",
                newName: "appeal_id");

            migrationBuilder.RenameIndex(
                name: "ix_digest_item_record_id",
                schema: "notify",
                table: "digest_item",
                newName: "ix_digest_item_appeal_id");
        }
    }
}
