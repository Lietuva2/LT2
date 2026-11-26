using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QueueKept : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "agenda_kept_for",
                schema: "appeals",
                table: "bill_record",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "agenda_kept_participants",
                schema: "appeals",
                table: "bill_record",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agenda_kept_stage",
                schema: "appeals",
                table: "bill_record",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "surge_kept_at",
                schema: "appeals",
                table: "bill_record",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agenda_kept_for",
                schema: "appeals",
                table: "bill_record");

            migrationBuilder.DropColumn(
                name: "agenda_kept_participants",
                schema: "appeals",
                table: "bill_record");

            migrationBuilder.DropColumn(
                name: "agenda_kept_stage",
                schema: "appeals",
                table: "bill_record");

            migrationBuilder.DropColumn(
                name: "surge_kept_at",
                schema: "appeals",
                table: "bill_record");
        }
    }
}
