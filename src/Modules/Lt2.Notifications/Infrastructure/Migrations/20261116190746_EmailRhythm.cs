using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EmailRhythm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Design 8.24: an MP's answer is an event of its own (key: person and moment), no longer a bill's day. Nothing is
            // launched, so the day records go rather than being converted.
            migrationBuilder.Sql("DELETE FROM notify.bill_event WHERE kind = 'response';");

            migrationBuilder.AddColumn<string>(
                name: "frequency",
                schema: "notify",
                table: "notice_setting",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "immediate");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "frequency_since",
                schema: "notify",
                table: "notice_setting",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "previous_frequency",
                schema: "notify",
                table: "notice_setting",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "previous_since",
                schema: "notify",
                table: "notice_setting",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "daily_at",
                schema: "notify",
                table: "bill_event",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "early_at",
                schema: "notify",
                table: "bill_event",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "weekly_at",
                schema: "notify",
                table: "bill_event",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "frequency",
                schema: "notify",
                table: "notice_setting");

            migrationBuilder.DropColumn(
                name: "frequency_since",
                schema: "notify",
                table: "notice_setting");

            migrationBuilder.DropColumn(
                name: "previous_frequency",
                schema: "notify",
                table: "notice_setting");

            migrationBuilder.DropColumn(
                name: "previous_since",
                schema: "notify",
                table: "notice_setting");

            migrationBuilder.DropColumn(
                name: "daily_at",
                schema: "notify",
                table: "bill_event");

            migrationBuilder.DropColumn(
                name: "early_at",
                schema: "notify",
                table: "bill_event");

            migrationBuilder.DropColumn(
                name: "weekly_at",
                schema: "notify",
                table: "bill_event");
        }
    }
}
