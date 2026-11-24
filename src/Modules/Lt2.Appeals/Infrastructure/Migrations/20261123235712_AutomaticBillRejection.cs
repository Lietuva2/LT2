using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticBillRejection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_automatic_rejection_vote_id",
                schema: "appeals",
                table: "bill_record",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_automatic_rejection_vote_id",
                schema: "appeals",
                table: "bill_record");
        }
    }
}
