using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DescriptionChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "description_change",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appeal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    screening_flags = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_description_change", x => x.id);
                    table.ForeignKey(
                        name: "fk_description_change_appeal_appeal_id",
                        column: x => x.appeal_id,
                        principalSchema: "appeals",
                        principalTable: "appeal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_description_change_appeal_id_created_at",
                schema: "appeals",
                table: "description_change",
                columns: new[] { "appeal_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_description_change_appeal_id_pending",
                schema: "appeals",
                table: "description_change",
                column: "appeal_id",
                unique: true,
                filter: "status = 'pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "description_change",
                schema: "appeals");
        }
    }
}
