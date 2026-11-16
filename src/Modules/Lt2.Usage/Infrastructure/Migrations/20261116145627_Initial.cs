using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Usage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "usage");

            migrationBuilder.CreateTable(
                name: "page",
                schema: "usage",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    views = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page", x => new { x.day, x.path });
                });

            migrationBuilder.CreateTable(
                name: "referrer",
                schema: "usage",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    views = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_referrer", x => new { x.day, x.host });
                });

            migrationBuilder.CreateTable(
                name: "sketch",
                schema: "usage",
                columns: table => new
                {
                    period = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    starts = table.Column<DateOnly>(type: "date", nullable: false),
                    audience = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    salt = table.Column<byte[]>(type: "bytea", nullable: false),
                    registers = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sketch", x => new { x.period, x.starts, x.audience });
                });

            migrationBuilder.CreateTable(
                name: "total",
                schema: "usage",
                columns: table => new
                {
                    period = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    starts = table.Column<DateOnly>(type: "date", nullable: false),
                    visitors = table.Column<int>(type: "integer", nullable: false),
                    members = table.Column<int>(type: "integer", nullable: false),
                    visitor_views = table.Column<int>(type: "integer", nullable: false),
                    member_views = table.Column<int>(type: "integer", nullable: false),
                    crawler_views = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_total", x => new { x.period, x.starts });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "page",
                schema: "usage");

            migrationBuilder.DropTable(
                name: "referrer",
                schema: "usage");

            migrationBuilder.DropTable(
                name: "sketch",
                schema: "usage");

            migrationBuilder.DropTable(
                name: "total",
                schema: "usage");
        }
    }
}
