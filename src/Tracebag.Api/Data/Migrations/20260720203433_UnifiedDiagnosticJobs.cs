using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tracebag.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UnifiedDiagnosticJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "diagnostic_job_id",
                table: "artifacts",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "manifest_file_name",
                table: "artifacts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sha256",
                table: "artifacts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state",
                table: "artifacts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "available");

            migrationBuilder.CreateTable(
                name: "diagnostic_jobs",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    container_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    container_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    docker_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    process_id = table.Column<int>(type: "integer", nullable: false),
                    profile = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    progress = table.Column<int>(type: "integer", nullable: false),
                    status_message = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deadline_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancel_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    inputs = table.Column<string>(type: "jsonb", nullable: false),
                    outcome = table.Column<string>(type: "jsonb", nullable: true),
                    error_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    error_message = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    runner_container_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    runtime_major = table.Column<int>(type: "integer", nullable: false),
                    runner_image = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    tool_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    artifact_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_diagnostic_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "diagnostic_job_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    progress = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_diagnostic_job_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_diagnostic_job_events_diagnostic_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "diagnostic_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_artifacts_diagnostic_job_id",
                table: "artifacts",
                column: "diagnostic_job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_diagnostic_job_events_job_id_id",
                table: "diagnostic_job_events",
                columns: new[] { "job_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_diagnostic_job_events_timestamp",
                table: "diagnostic_job_events",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_diagnostic_jobs_artifact_id",
                table: "diagnostic_jobs",
                column: "artifact_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_diagnostic_jobs_container_id_status",
                table: "diagnostic_jobs",
                columns: new[] { "container_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_diagnostic_jobs_created_at_status",
                table: "diagnostic_jobs",
                columns: new[] { "created_at", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_diagnostic_jobs_idempotency_key",
                table: "diagnostic_jobs",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_diagnostic_jobs_one_active_target",
                table: "diagnostic_jobs",
                column: "container_id",
                unique: true,
                filter: "status IN ('queued', 'validating', 'starting', 'running', 'collecting', 'stopping')");

            migrationBuilder.CreateIndex(
                name: "IX_diagnostic_jobs_runner_container_id",
                table: "diagnostic_jobs",
                column: "runner_container_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "diagnostic_job_events");

            migrationBuilder.DropTable(
                name: "diagnostic_jobs");

            migrationBuilder.DropIndex(
                name: "IX_artifacts_diagnostic_job_id",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "diagnostic_job_id",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "manifest_file_name",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "sha256",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "state",
                table: "artifacts");
        }
    }
}
