using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProposalsPerBill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_appeal_bill_number",
                schema: "appeals",
                table: "appeal");

            migrationBuilder.CreateIndex(
                name: "ix_appeal_bill_number_initiator_waiting",
                schema: "appeals",
                table: "appeal",
                columns: new[] { "bill_number", "initiator_user_id" },
                unique: true,
                filter: "status IN ('pending', 'returned')");

            migrationBuilder.CreateIndex(
                name: "ix_appeal_bill_number_published",
                schema: "appeals",
                table: "appeal",
                column: "bill_number",
                unique: true,
                filter: "status IN ('open', 'closed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_appeal_bill_number_initiator_waiting",
                schema: "appeals",
                table: "appeal");

            migrationBuilder.DropIndex(
                name: "ix_appeal_bill_number_published",
                schema: "appeals",
                table: "appeal");

            migrationBuilder.CreateIndex(
                name: "ix_appeal_bill_number",
                schema: "appeals",
                table: "appeal",
                column: "bill_number",
                unique: true,
                filter: "status <> 'rejected'");
        }
    }
}
