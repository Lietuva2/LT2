using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PendingAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "invitation_code",
                schema: "identity",
                table: "profile",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "registered_at",
                schema: "identity",
                table: "profile",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_profile_pending_created_at",
                schema: "identity",
                table: "profile",
                column: "created_at",
                filter: "registered_at IS NULL");

            // Accounts that confirmed their address under the earlier rules gave their name and age declaration at
            // registration and are members already: they count as completed. Unconfirmed ones stay pending.
            migrationBuilder.Sql(
                "UPDATE identity.profile SET registered_at = created_at WHERE verification_level = 'email' AND registered_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_profile_pending_created_at",
                schema: "identity",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "invitation_code",
                schema: "identity",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "registered_at",
                schema: "identity",
                table: "profile");
        }
    }
}
