using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <summary>
    /// Owner, 2026-11-19: a record has no initiator. The column nothing sets goes, and a description an initiator wrote
    /// before 2026-11-14 (approved by a moderator) is labelled as a moderator's, as the demonstration seeding repaired it.
    /// </summary>
    public partial class DropInitiator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE appeals.bill_record SET description_source = 'moderator', description_at = coalesce(description_at, published_at) WHERE description_source = 'initiator'");

            migrationBuilder.DropColumn(
                name: "initiator_user_id",
                schema: "appeals",
                table: "bill_record");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "initiator_user_id",
                schema: "appeals",
                table: "bill_record",
                type: "uuid",
                nullable: true);
        }
    }
}
