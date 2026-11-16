using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MpResponses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mp_response",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bill_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    seimas_person_id = table.Column<long>(type: "bigint", nullable: false),
                    stance = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    text = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    screening_flags = table.Column<string[]>(type: "text[]", nullable: false),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hidden_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hide_statement = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    written_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mp_response", x => x.id);
                    table.CheckConstraint("ck_mp_response_stance", "stance IN ('for', 'against', 'undecided')");
                    table.CheckConstraint("ck_mp_response_status", "status IN ('published', 'withdrawn', 'hidden')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_mp_response_bill_number",
                schema: "appeals",
                table: "mp_response",
                column: "bill_number");

            migrationBuilder.CreateIndex(
                name: "ix_mp_response_one_per_bill",
                schema: "appeals",
                table: "mp_response",
                columns: new[] { "seimas_person_id", "bill_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mp_response_written_by_user_id",
                schema: "appeals",
                table: "mp_response",
                column: "written_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mp_response",
                schema: "appeals");
        }
    }
}
