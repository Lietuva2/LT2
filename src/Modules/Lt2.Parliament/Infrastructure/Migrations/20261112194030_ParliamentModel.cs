using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lt2.Parliament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ParliamentModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "parliament");

            migrationBuilder.CreateTable(
                name: "bill",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    e_seimas_url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bill", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fetch_state",
                schema: "parliament",
                columns: table => new
                {
                    request = table.Column<string>(type: "text", nullable: false),
                    raw_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    parsed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    parser_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fetch_state", x => x.request);
                });

            migrationBuilder.CreateTable(
                name: "import_run",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    synthetic = table.Column<bool>(type: "boolean", nullable: false),
                    requests = table.Column<int>(type: "integer", nullable: false),
                    changes = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "person",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seimas_person_id = table.Column<long>(type: "bigint", nullable: false),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    gender = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_person", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "raw_document",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request = table.Column<string>(type: "text", nullable: false),
                    service = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<byte[]>(type: "bytea", nullable: false),
                    synthetic = table.Column<bool>(type: "boolean", nullable: false),
                    import_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raw_document", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "term",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_term", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contact",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_person_person_id",
                        column: x => x.person_id,
                        principalSchema: "parliament",
                        principalTable: "person",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "body",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seimas_id = table.Column<long>(type: "bigint", nullable: false),
                    term_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_body", x => x.id);
                    table.ForeignKey(
                        name: "fk_body_term_term_id",
                        column: x => x.term_id,
                        principalSchema: "parliament",
                        principalTable: "term",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mandate",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<int>(type: "integer", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: true),
                    nominating_party = table.Column<string>(type: "text", nullable: true),
                    elected_how = table.Column<string>(type: "text", nullable: true),
                    constituency = table.Column<string>(type: "text", nullable: true),
                    terms_served = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mandate", x => x.id);
                    table.ForeignKey(
                        name: "fk_mandate_person_person_id",
                        column: x => x.person_id,
                        principalSchema: "parliament",
                        principalTable: "person",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mandate_term_term_id",
                        column: x => x.term_id,
                        principalSchema: "parliament",
                        principalTable: "term",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mp_stat",
                schema: "parliament",
                columns: table => new
                {
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<int>(type: "integer", nullable: false),
                    votes_held = table.Column<int>(type: "integer", nullable: false),
                    votes_taken_part = table.Column<int>(type: "integer", nullable: false),
                    votes_with_faction_position = table.Column<int>(type: "integer", nullable: false),
                    voted_with_faction = table.Column<int>(type: "integer", nullable: false),
                    free_votes = table.Column<int>(type: "integer", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mp_stat", x => new { x.person_id, x.term_id });
                    table.ForeignKey(
                        name: "fk_mp_stat_person_person_id",
                        column: x => x.person_id,
                        principalSchema: "parliament",
                        principalTable: "person",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mp_stat_term_term_id",
                        column: x => x.term_id,
                        principalSchema: "parliament",
                        principalTable: "term",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "session",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    term_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session", x => x.id);
                    table.ForeignKey(
                        name: "fk_session_term_term_id",
                        column: x => x.term_id,
                        principalSchema: "parliament",
                        principalTable: "term",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "membership",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membership", x => x.id);
                    table.ForeignKey(
                        name: "fk_membership_body_body_id",
                        column: x => x.body_id,
                        principalSchema: "parliament",
                        principalTable: "body",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_membership_person_person_id",
                        column: x => x.person_id,
                        principalSchema: "parliament",
                        principalTable: "person",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sitting",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    session_id = table.Column<long>(type: "bigint", nullable: false),
                    number = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sitting", x => x.id);
                    table.ForeignKey(
                        name: "fk_sitting_session_session_id",
                        column: x => x.session_id,
                        principalSchema: "parliament",
                        principalTable: "session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agenda_item",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    sitting_id = table.Column<long>(type: "bigint", nullable: false),
                    bill_id = table.Column<Guid>(type: "uuid", nullable: true),
                    number = table.Column<string>(type: "text", nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    stage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agenda_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_agenda_item_bill_bill_id",
                        column: x => x.bill_id,
                        principalSchema: "parliament",
                        principalTable: "bill",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_agenda_item_sitting_sitting_id",
                        column: x => x.sitting_id,
                        principalSchema: "parliament",
                        principalTable: "sitting",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote",
                schema: "parliament",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    agenda_item_id = table.Column<long>(type: "bigint", nullable: false),
                    bill_id = table.Column<Guid>(type: "uuid", nullable: true),
                    held_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    wording = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    is_consensus = table.Column<bool>(type: "boolean", nullable: false),
                    has_result = table.Column<bool>(type: "boolean", nullable: false),
                    @for = table.Column<int>(name: "for", type: "integer", nullable: true),
                    against = table.Column<int>(type: "integer", nullable: true),
                    abstain = table.Column<int>(type: "integer", nullable: true),
                    voted = table.Column<int>(type: "integer", nullable: true),
                    seats = table.Column<int>(type: "integer", nullable: true),
                    official_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vote", x => x.id);
                    table.ForeignKey(
                        name: "fk_vote_agenda_item_agenda_item_id",
                        column: x => x.agenda_item_id,
                        principalSchema: "parliament",
                        principalTable: "agenda_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vote_bill_bill_id",
                        column: x => x.bill_id,
                        principalSchema: "parliament",
                        principalTable: "bill",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ballot",
                schema: "parliament",
                columns: table => new
                {
                    vote_id = table.Column<long>(type: "bigint", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    faction_label = table.Column<string>(type: "text", nullable: true),
                    faction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    choice = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ballot", x => new { x.vote_id, x.person_id });
                    table.ForeignKey(
                        name: "fk_ballot_body_faction_id",
                        column: x => x.faction_id,
                        principalSchema: "parliament",
                        principalTable: "body",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ballot_person_person_id",
                        column: x => x.person_id,
                        principalSchema: "parliament",
                        principalTable: "person",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ballot_vote_vote_id",
                        column: x => x.vote_id,
                        principalSchema: "parliament",
                        principalTable: "vote",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "faction_position",
                schema: "parliament",
                columns: table => new
                {
                    vote_id = table.Column<long>(type: "bigint", nullable: false),
                    faction_label = table.Column<string>(type: "text", nullable: false),
                    faction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    @for = table.Column<int>(name: "for", type: "integer", nullable: false),
                    against = table.Column<int>(type: "integer", nullable: false),
                    abstain = table.Column<int>(type: "integer", nullable: false),
                    did_not_vote = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_faction_position", x => new { x.vote_id, x.faction_label });
                    table.ForeignKey(
                        name: "fk_faction_position_body_faction_id",
                        column: x => x.faction_id,
                        principalSchema: "parliament",
                        principalTable: "body",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_faction_position_vote_vote_id",
                        column: x => x.vote_id,
                        principalSchema: "parliament",
                        principalTable: "vote",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_item_bill_id",
                schema: "parliament",
                table: "agenda_item",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_item_sitting_id",
                schema: "parliament",
                table: "agenda_item",
                column: "sitting_id");

            migrationBuilder.CreateIndex(
                name: "ix_ballot_faction_id",
                schema: "parliament",
                table: "ballot",
                column: "faction_id");

            migrationBuilder.CreateIndex(
                name: "ix_ballot_person_id",
                schema: "parliament",
                table: "ballot",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_bill_number",
                schema: "parliament",
                table: "bill",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_body_term_id_seimas_id",
                schema: "parliament",
                table: "body",
                columns: new[] { "term_id", "seimas_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_body_term_id_slug",
                schema: "parliament",
                table: "body",
                columns: new[] { "term_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contact_person_id_kind_value",
                schema: "parliament",
                table: "contact",
                columns: new[] { "person_id", "kind", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_faction_position_faction_id",
                schema: "parliament",
                table: "faction_position",
                column: "faction_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_run_started_at",
                schema: "parliament",
                table: "import_run",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_mandate_person_id_term_id",
                schema: "parliament",
                table: "mandate",
                columns: new[] { "person_id", "term_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mandate_term_id",
                schema: "parliament",
                table: "mandate",
                column: "term_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_body_id",
                schema: "parliament",
                table: "membership",
                column: "body_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_person_id_body_id_from",
                schema: "parliament",
                table: "membership",
                columns: new[] { "person_id", "body_id", "from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mp_stat_term_id",
                schema: "parliament",
                table: "mp_stat",
                column: "term_id");

            migrationBuilder.CreateIndex(
                name: "ix_person_seimas_person_id",
                schema: "parliament",
                table: "person",
                column: "seimas_person_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_person_slug",
                schema: "parliament",
                table: "person",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_raw_document_request_sha256",
                schema: "parliament",
                table: "raw_document",
                columns: new[] { "request", "sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_raw_document_synthetic",
                schema: "parliament",
                table: "raw_document",
                column: "synthetic",
                filter: "synthetic");

            migrationBuilder.CreateIndex(
                name: "ix_session_term_id",
                schema: "parliament",
                table: "session",
                column: "term_id");

            migrationBuilder.CreateIndex(
                name: "ix_sitting_session_id",
                schema: "parliament",
                table: "sitting",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_sitting_starts_at",
                schema: "parliament",
                table: "sitting",
                column: "starts_at");

            migrationBuilder.CreateIndex(
                name: "ix_vote_agenda_item_id",
                schema: "parliament",
                table: "vote",
                column: "agenda_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_vote_bill_id",
                schema: "parliament",
                table: "vote",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_vote_held_at",
                schema: "parliament",
                table: "vote",
                column: "held_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ballot",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "contact",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "faction_position",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "fetch_state",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "import_run",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "mandate",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "membership",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "mp_stat",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "raw_document",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "vote",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "body",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "person",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "agenda_item",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "bill",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "sitting",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "session",
                schema: "parliament");

            migrationBuilder.DropTable(
                name: "term",
                schema: "parliament");
        }
    }
}
