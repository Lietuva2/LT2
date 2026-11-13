using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersonalInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "identity",
                table: "invitation",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "team");

            migrationBuilder.AddColumn<int>(
                name: "level",
                schema: "identity",
                table: "invitation",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "replaced_at",
                schema: "identity",
                table: "invitation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "switched_off_at",
                schema: "identity",
                table: "invitation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "used_count",
                schema: "identity",
                table: "invitation",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "lookup",
                schema: "identity",
                table: "invitation",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                computedColumnSql: "upper(replace(replace(code, '-', ''), ' ', ''))",
                stored: true);

            migrationBuilder.CreateTable(
                name: "invitation_daily",
                schema: "identity",
                columns: table => new
                {
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    completed = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitation_daily", x => new { x.invitation_id, x.day });
                    table.ForeignKey(
                        name: "fk_invitation_daily_invitation_invitation_id",
                        column: x => x.invitation_id,
                        principalSchema: "identity",
                        principalTable: "invitation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invitation_issued_by_user_id",
                schema: "identity",
                table: "invitation",
                column: "issued_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_lookup",
                schema: "identity",
                table: "invitation",
                column: "lookup",
                unique: true);

            // Existing team codes: the registrations already completed with them (design 6.2: the counter never drops).
            migrationBuilder.Sql(
                """
                UPDATE identity.invitation i
                SET used_count = (SELECT count(*) FROM identity.profile p WHERE p.invitation_id = i.id AND p.registered_at IS NOT NULL)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invitation_daily",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "ix_invitation_issued_by_user_id",
                schema: "identity",
                table: "invitation");

            migrationBuilder.DropIndex(
                name: "ix_invitation_lookup",
                schema: "identity",
                table: "invitation");

            migrationBuilder.DropColumn(
                name: "lookup",
                schema: "identity",
                table: "invitation");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "identity",
                table: "invitation");

            migrationBuilder.DropColumn(
                name: "level",
                schema: "identity",
                table: "invitation");

            migrationBuilder.DropColumn(
                name: "replaced_at",
                schema: "identity",
                table: "invitation");

            migrationBuilder.DropColumn(
                name: "switched_off_at",
                schema: "identity",
                table: "invitation");

            migrationBuilder.DropColumn(
                name: "used_count",
                schema: "identity",
                table: "invitation");
        }
    }
}
