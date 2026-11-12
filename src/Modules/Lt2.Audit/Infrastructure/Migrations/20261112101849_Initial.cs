using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "config");

            migrationBuilder.CreateTable(
                name: "event",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    ip_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "setting",
                schema: "config",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_setting", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_occurred_at",
                schema: "audit",
                table: "event",
                column: "occurred_at");

            // Append-only even for roles that could otherwise change rows (design 5.5); the grants are the first guard.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION audit.forbid_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit.event is append-only';
                END;
                $$;
                CREATE TRIGGER event_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON audit.event
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.forbid_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS event_append_only ON audit.event; DROP FUNCTION IF EXISTS audit.forbid_change();");

            migrationBuilder.DropTable(
                name: "event",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "setting",
                schema: "config");
        }
    }
}
