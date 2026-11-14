using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Appeals.Infrastructure.Migrations
{
    /// <summary>
    /// Design 8.12: one record per bill, nobody proposes or asks for an appeal. Proposals still waiting or returned are
    /// closed without emails (status superseded), requests and proposed description changes are deleted, a threshold an
    /// initiator chose becomes the default, and older hidden appeals on a bill that has another record stop being its
    /// record. The local stack is the only deployment, so no notice is owed (8.12.4).
    /// </summary>
    public partial class OneRecordPerBill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appeal_request",
                schema: "appeals");

            migrationBuilder.DropTable(
                name: "description_change",
                schema: "appeals");

            migrationBuilder.DropIndex(
                name: "ix_appeal_bill_number_initiator_waiting",
                schema: "appeals",
                table: "appeal");

            migrationBuilder.DropIndex(
                name: "ix_appeal_bill_number_published",
                schema: "appeals",
                table: "appeal");

            migrationBuilder.DropColumn(
                name: "return_note",
                schema: "appeals",
                table: "appeal");

            migrationBuilder.DropColumn(
                name: "screening_flags",
                schema: "appeals",
                table: "appeal");

            migrationBuilder.AlterColumn<int>(
                name: "threshold",
                schema: "appeals",
                table: "appeal",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "description_source",
                schema: "appeals",
                table: "appeal",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "initiator");

            // Proposals that were waiting or returned close without emails; one hidden before publication was a proposal too.
            migrationBuilder.Sql("UPDATE appeals.appeal SET status = 'superseded', changed_at = now() WHERE status IN ('pending', 'returned')");
            migrationBuilder.Sql("UPDATE appeals.appeal SET status = 'superseded', changed_at = now() WHERE status = 'rejected' AND published_at IS NULL");

            // One record per bill: a hidden appeal stays the bill's record only when the bill has no open or closed one and it
            // is the latest hidden one there.
            migrationBuilder.Sql(
                """
                UPDATE appeals.appeal r SET status = 'superseded', changed_at = now()
                WHERE r.status = 'rejected'
                  AND (EXISTS (SELECT 1 FROM appeals.appeal o WHERE o.bill_number = r.bill_number AND o.status IN ('open', 'closed'))
                       OR EXISTS (SELECT 1 FROM appeals.appeal o WHERE o.bill_number = r.bill_number AND o.status = 'rejected'
                                  AND (o.changed_at > r.changed_at OR (o.changed_at = r.changed_at AND o.id > r.id))))
                """);

            // A threshold an initiator chose becomes the default of all bills (design 8.12.4); a moderator's number stays.
            migrationBuilder.Sql("UPDATE appeals.appeal SET threshold = NULL, threshold_set_by = 'default' WHERE threshold_set_by = 'initiator'");

            migrationBuilder.CreateIndex(
                name: "ix_appeal_bill_number_record",
                schema: "appeals",
                table: "appeal",
                column: "bill_number",
                unique: true,
                filter: "status IN ('open', 'closed', 'rejected')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_appeal_bill_number_record",
                schema: "appeals",
                table: "appeal");

            migrationBuilder.AlterColumn<int>(
                name: "threshold",
                schema: "appeals",
                table: "appeal",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "description_source",
                schema: "appeals",
                table: "appeal",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "initiator",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_note",
                schema: "appeals",
                table: "appeal",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "screening_flags",
                schema: "appeals",
                table: "appeal",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateTable(
                name: "appeal_request",
                schema: "appeals",
                columns: table => new
                {
                    bill_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appeal_request", x => new { x.bill_number, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "description_change",
                schema: "appeals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appeal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    screening_flags = table.Column<string[]>(type: "text[]", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    text = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_description_change", x => x.id);
                    table.ForeignKey(
                        name: "fk_description_change_appeal_appeal_id",
                        column: x => x.appeal_id,
                        principalSchema: "appeals",
                        principalTable: "appeal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appeal_bill_number_initiator_waiting",
                schema: "appeals",
                table: "appeal",
                columns: new[] { "bill_number", "initiator_user_id" },
                unique: true,
                filter: "status IN ('pending', 'returned')");

            migrationBuilder.CreateIndex(
                name: "ix_appeal_bill_number_published",
                schema: "appeals",
                table: "appeal",
                column: "bill_number",
                unique: true,
                filter: "status IN ('open', 'closed')");

            migrationBuilder.CreateIndex(
                name: "ix_appeal_request_user_id",
                schema: "appeals",
                table: "appeal_request",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_description_change_appeal_id_created_at",
                schema: "appeals",
                table: "description_change",
                columns: new[] { "appeal_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_description_change_appeal_id_pending",
                schema: "appeals",
                table: "description_change",
                column: "appeal_id",
                unique: true,
                filter: "status = 'pending'");
        }
    }
}
