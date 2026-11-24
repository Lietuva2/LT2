using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BillCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The trigram index of the part-of-a-word search (design 8.12.5); in this module's schema, so no other module sees it.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm SCHEMA appeals;");

            migrationBuilder.CreateTable(
                name: "bill_card",
                schema: "appeals",
                columns: table => new
                {
                    bill_number = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    plain_title = table.Column<string>(type: "character varying(90)", maxLength: 90, nullable: true),
                    title_folded = table.Column<string>(type: "text", nullable: false),
                    plain_title_folded = table.Column<string>(type: "text", nullable: true),
                    search_folded = table.Column<string>(type: "text", nullable: false, computedColumnSql: "title_folded || ' ' || coalesce(plain_title_folded, '')", stored: true),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false, computedColumnSql: "to_tsvector('lithuanian'::regconfig, bill_number || ' ' || title || ' ' || coalesce(plain_title, ''))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bill_card", x => x.bill_number);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bill_card_search_folded",
                schema: "appeals",
                table: "bill_card",
                column: "search_folded")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "appeals.gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_bill_card_search_vector",
                schema: "appeals",
                table: "bill_card",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bill_card",
                schema: "appeals");
        }
    }
}
