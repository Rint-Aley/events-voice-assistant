using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace TaskManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "turns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    state_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "text", nullable: true),
                    audio_object_key = table.Column<string>(type: "text", nullable: true),
                    stt_confidence = table.Column<decimal>(type: "numeric(4,3)", nullable: true),
                    plan = table.Column<string>(type: "jsonb", nullable: true),
                    error_code = table.Column<string>(type: "text", nullable: true),
                    deadline_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_turns", x => x.id);
                    table.CheckConstraint("turns_state_check", "state in ('TRANSCRIBING', 'INTERPRETING', 'APPLYING', 'COMPLETED', 'NEEDS_CLARIFICATION', 'FAILED', 'EXPIRED')");
                    table.CheckConstraint("turns_state_version_check", "state_version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    turn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    total_count = table.Column<int>(type: "integer", nullable: true),
                    warnings = table.Column<string>(type: "jsonb", nullable: false),
                    audio_object_key = table.Column<string>(type: "text", nullable: true),
                    audio_status = table.Column<string>(type: "text", nullable: false, defaultValue: "skipped"),
                    audio_duration_ms = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_answers", x => x.id);
                    table.CheckConstraint("answers_audio_status_check", "audio_status in ('pending', 'ready', 'failed', 'skipped')");
                    table.CheckConstraint("answers_total_count_check", "total_count is null or total_count >= 0");
                    table.ForeignKey(
                        name: "FK_answers_turns_turn_id",
                        column: x => x.turn_id,
                        principalTable: "turns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "domain_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    turn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<short>(type: "smallint", nullable: false),
                    op = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "pending"),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_domain_commands", x => x.id);
                    table.CheckConstraint("domain_commands_attempts_check", "attempts >= 0");
                    table.CheckConstraint("domain_commands_op_check", "op in ('event.create', 'event.update')");
                    table.CheckConstraint("domain_commands_status_check", "status in ('pending', 'applied', 'failed')");
                    table.ForeignKey(
                        name: "FK_domain_commands_turns_turn_id",
                        column: x => x.turn_id,
                        principalTable: "turns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    turn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    source_text = table.Column<string>(type: "text", nullable: true),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: true),
                    starts_at = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    is_deadline = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    clarification_round = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    embedding = table.Column<Vector>(type: "vector", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drafts", x => x.id);
                    table.CheckConstraint("drafts_clarification_round_check", "clarification_round between 0 and 2");
                    table.CheckConstraint("drafts_duration_positive", "duration is null or duration > interval '0'");
                    table.CheckConstraint("drafts_duration_requires_time", "duration is null or starts_at is not null");
                    table.CheckConstraint("drafts_kind_check", "kind is null or kind in ('task', 'reminder', 'meeting')");
                    table.CheckConstraint("drafts_time_requires_date", "starts_at is null or starts_on is not null");
                    table.ForeignKey(
                        name: "FK_drafts_turns_turn_id",
                        column: x => x.turn_id,
                        principalTable: "turns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    kind = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "active"),
                    description = table.Column<string>(type: "text", nullable: false),
                    source_text = table.Column<string>(type: "text", nullable: true),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: true),
                    starts_at = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    is_deadline = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    embedding = table.Column<Vector>(type: "vector", nullable: true),
                    embedding_model = table.Column<string>(type: "text", nullable: true),
                    source_turn_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<string>(type: "text", nullable: false, defaultValue: "demo-user")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.id);
                    table.CheckConstraint("events_deadline_only_task", "not is_deadline or kind = 'task'");
                    table.CheckConstraint("events_description_not_blank", "length(btrim(description)) > 0");
                    table.CheckConstraint("events_duration_only_meeting", "duration is null or kind = 'meeting'");
                    table.CheckConstraint("events_duration_positive", "duration is null or duration > interval '0'");
                    table.CheckConstraint("events_duration_requires_time", "duration is null or starts_at is not null");
                    table.CheckConstraint("events_kind_check", "kind in ('task', 'reminder', 'meeting')");
                    table.CheckConstraint("events_model_pair", "(embedding is null) = (embedding_model is null)");
                    table.CheckConstraint("events_status_check", "status in ('active', 'done', 'cancelled')");
                    table.CheckConstraint("events_time_requires_date", "starts_at is null or starts_on is not null");
                    table.ForeignKey(
                        name: "FK_events_turns_source_turn_id",
                        column: x => x.source_turn_id,
                        principalTable: "turns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "answer_events",
                columns: table => new
                {
                    answer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_answer_events", x => new { x.answer_id, x.event_id });
                    table.ForeignKey(
                        name: "FK_answer_events_answers_answer_id",
                        column: x => x.answer_id,
                        principalTable: "answers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_answer_events_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_answer_events_event_id",
                table: "answer_events",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "answers_turn_id_key",
                table: "answers",
                column: "turn_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "domain_commands_pending",
                table: "domain_commands",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "domain_commands_seq_unique",
                table: "domain_commands",
                columns: new[] { "turn_id", "seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "drafts_turn_id_key",
                table: "drafts",
                column: "turn_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "events_by_date",
                table: "events",
                columns: new[] { "starts_on", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "events_by_source_turn",
                table: "events",
                column: "source_turn_id");

            migrationBuilder.CreateIndex(
                name: "events_by_status",
                table: "events",
                columns: new[] { "status", "starts_on" });

            migrationBuilder.CreateIndex(
                name: "turns_deadline",
                table: "turns",
                columns: new[] { "state", "deadline_at" });

            migrationBuilder.CreateIndex(
                name: "turns_idempotency_key_key",
                table: "turns",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "turns_one_active",
                table: "turns",
                column: "session_id",
                unique: true,
                filter: "state in ('TRANSCRIBING', 'INTERPRETING', 'APPLYING')");

            migrationBuilder.CreateIndex(
                name: "turns_recent",
                table: "turns",
                column: "created_at",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "answer_events");

            migrationBuilder.DropTable(
                name: "domain_commands");

            migrationBuilder.DropTable(
                name: "drafts");

            migrationBuilder.DropTable(
                name: "answers");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "turns");
        }
    }
}
