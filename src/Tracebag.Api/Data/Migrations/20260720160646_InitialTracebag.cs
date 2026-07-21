using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tracebag.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialTracebag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "artifacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    container_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    container_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    created_by = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_artifacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    action = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    target_container_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    target_container_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    result = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "counter_recording_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    container_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    container_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    runner_container_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    process_id = table.Column<int>(type: "integer", nullable: false),
                    preset = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    max_duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stopped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_sample_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sample_count = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_by = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    stop_reason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    error_message = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    providers = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counter_recording_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "counter_rollups_1m",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    container_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    counter_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    bucket_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    average = table.Column<double>(type: "double precision", nullable: false),
                    minimum = table.Column<double>(type: "double precision", nullable: false),
                    maximum = table.Column<double>(type: "double precision", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counter_rollups_1m", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "log_checkpoints",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    container_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    last_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_log_checkpoints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "log_streams",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    container_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    container_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    image = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    labels = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_log_streams", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "counter_samples",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provider = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    counter_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    value = table.Column<double>(type: "double precision", nullable: false),
                    tags = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counter_samples", x => x.id);
                    table.ForeignKey(
                        name: "FK_counter_samples_counter_recording_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "counter_recording_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "log_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    log_stream_id = table.Column<long>(type: "bigint", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stream = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    line = table.Column<string>(type: "text", nullable: false),
                    level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    exception_type = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    parsed_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_log_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_log_entries_log_streams_log_stream_id",
                        column: x => x.log_stream_id,
                        principalTable: "log_streams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_artifacts_container_id",
                table: "artifacts",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "IX_artifacts_created_at",
                table: "artifacts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_artifacts_expires_at",
                table: "artifacts",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_action",
                table: "audit_events",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_target_container_id",
                table: "audit_events",
                column: "target_container_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_timestamp",
                table: "audit_events",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_counter_recording_sessions_container_id",
                table: "counter_recording_sessions",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "IX_counter_recording_sessions_started_at",
                table: "counter_recording_sessions",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "IX_counter_recording_sessions_status",
                table: "counter_recording_sessions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_counter_rollups_1m_container_id_bucket_start",
                table: "counter_rollups_1m",
                columns: new[] { "container_id", "bucket_start" });

            migrationBuilder.CreateIndex(
                name: "IX_counter_rollups_1m_provider_name",
                table: "counter_rollups_1m",
                columns: new[] { "provider", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_counter_samples_provider_name",
                table: "counter_samples",
                columns: new[] { "provider", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_counter_samples_session_id_captured_at",
                table: "counter_samples",
                columns: new[] { "session_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "IX_log_checkpoints_container_id",
                table: "log_checkpoints",
                column: "container_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_log_entries_level",
                table: "log_entries",
                column: "level");

            migrationBuilder.CreateIndex(
                name: "IX_log_entries_log_stream_id_received_at",
                table: "log_entries",
                columns: new[] { "log_stream_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_log_entries_received_at",
                table: "log_entries",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "IX_log_streams_container_id",
                table: "log_streams",
                column: "container_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "artifacts");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "counter_rollups_1m");

            migrationBuilder.DropTable(
                name: "counter_samples");

            migrationBuilder.DropTable(
                name: "log_checkpoints");

            migrationBuilder.DropTable(
                name: "log_entries");

            migrationBuilder.DropTable(
                name: "counter_recording_sessions");

            migrationBuilder.DropTable(
                name: "log_streams");
        }
    }
}
