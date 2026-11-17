using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LastActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_activity_at",
                schema: "appeals",
                table: "bill_record",
                type: "timestamp with time zone",
                nullable: true);

            // Design 8.12.5 (owner, 2026-11-17): the last activity of what is there, so a local stack's bills sort at once.
            migrationBuilder.Sql(
                """
                UPDATE appeals.bill_record r SET last_activity_at = a.at FROM (
                    SELECT x.id, max(x.at) AS at FROM (
                        SELECT record_id AS id, changed_at AS at FROM appeals.position
                        UNION ALL SELECT record_id, first_public_at FROM appeals.reason WHERE status = 'published'
                        UNION ALL SELECT b.id, c.created_at FROM appeals.comment c JOIN appeals.bill_record b ON b.bill_number = c.bill_number AND b.status = 'open'
                        UNION ALL SELECT b.id, coalesce(s.edited_at, s.first_public_at) FROM appeals.organisation_statement s
                            JOIN appeals.bill_record b ON b.bill_number = s.bill_number AND b.status = 'open' WHERE s.status = 'published'
                        UNION ALL SELECT b.id, coalesce(m.edited_at, m.published_at) FROM appeals.mp_response m
                            JOIN appeals.bill_record b ON b.bill_number = m.bill_number AND b.status = 'open' WHERE m.status = 'published'
                    ) x WHERE x.at IS NOT NULL GROUP BY x.id
                ) a WHERE r.id = a.id AND r.status = 'open';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_activity_at",
                schema: "appeals",
                table: "bill_record");
        }
    }
}
