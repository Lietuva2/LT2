using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <summary>
    /// Owner, 2026-11-19 (design 8.1 step 6): a record also closes when a moderator records that the Seimas rejected the bill or
    /// its initiators withdrew it. Why it closed (<c>vote</c>, <c>rejected</c>, <c>withdrawn</c>), the day of that decision and the
    /// source on lrs.lt; records closed by a vote say so.
    /// </summary>
    public partial class RecordClosedReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "closed_decided_on",
                schema: "appeals",
                table: "bill_record",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "closed_reason",
                schema: "appeals",
                table: "bill_record",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "closed_source_url",
                schema: "appeals",
                table: "bill_record",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql("UPDATE appeals.bill_record SET closed_reason = 'vote' WHERE closed_at IS NOT NULL AND closing_vote_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "closed_decided_on",
                schema: "appeals",
                table: "bill_record");

            migrationBuilder.DropColumn(
                name: "closed_reason",
                schema: "appeals",
                table: "bill_record");

            migrationBuilder.DropColumn(
                name: "closed_source_url",
                schema: "appeals",
                table: "bill_record");
        }
    }
}
