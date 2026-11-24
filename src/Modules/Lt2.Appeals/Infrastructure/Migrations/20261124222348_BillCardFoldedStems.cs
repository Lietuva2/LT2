using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BillCardFoldedStems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector_folded",
                schema: "appeals",
                table: "bill_card",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "to_tsvector('lithuanian'::regconfig, title_folded || ' ' || coalesce(plain_title_folded, ''))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_bill_card_search_vector_folded",
                schema: "appeals",
                table: "bill_card",
                column: "search_vector_folded")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bill_card_search_vector_folded",
                schema: "appeals",
                table: "bill_card");

            migrationBuilder.DropColumn(
                name: "search_vector_folded",
                schema: "appeals",
                table: "bill_card");
        }
    }
}
