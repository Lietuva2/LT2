using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrganisationInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "invitation_level",
                schema: "identity",
                table: "profile",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "organisation_id",
                schema: "identity",
                table: "invitation",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "organisation_off",
                schema: "identity",
                table: "invitation",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_invitation_organisation_current",
                schema: "identity",
                table: "invitation",
                column: "organisation_id",
                unique: true,
                filter: "kind = 'organisation' AND replaced_at IS NULL");

            // Completed profiles keep the level of the code they registered with (design 6.2); for an organisation's code
            // (design 8.14.14, stage B) it is all that is kept.
            migrationBuilder.Sql(
                """
                UPDATE identity.profile p
                SET invitation_level = i.level
                FROM identity.invitation i
                WHERE p.invitation_id = i.id AND p.registered_at IS NOT NULL
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invitation_organisation_current",
                schema: "identity",
                table: "invitation");

            migrationBuilder.DropColumn(
                name: "invitation_level",
                schema: "identity",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "organisation_id",
                schema: "identity",
                table: "invitation");

            migrationBuilder.DropColumn(
                name: "organisation_off",
                schema: "identity",
                table: "invitation");
        }
    }
}
