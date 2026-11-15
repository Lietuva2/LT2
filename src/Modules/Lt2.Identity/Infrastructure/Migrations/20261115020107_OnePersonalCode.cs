using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OnePersonalCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Design 6.8: one current personal code per member. A member who got two codes from parallel requests keeps the
            // newest; the older ones are marked replaced (they stop working, their counts stay), as replacing a code does.
            migrationBuilder.Sql(
                """
                UPDATE identity.invitation i
                SET replaced_at = now()
                WHERE i.kind = 'personal' AND i.replaced_at IS NULL AND i.issued_by_user_id IS NOT NULL
                  AND EXISTS (
                      SELECT 1 FROM identity.invitation newer
                      WHERE newer.kind = 'personal' AND newer.replaced_at IS NULL
                        AND newer.issued_by_user_id = i.issued_by_user_id
                        AND (newer.created_at, newer.id) > (i.created_at, i.id));
                """);
            migrationBuilder.CreateIndex(
                name: "ix_invitation_personal_current",
                schema: "identity",
                table: "invitation",
                column: "issued_by_user_id",
                unique: true,
                filter: "kind = 'personal' AND replaced_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invitation_personal_current",
                schema: "identity",
                table: "invitation");
        }
    }
}
