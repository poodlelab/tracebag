using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tracebag.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class IncidentsAndTracebags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incidents",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    container_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    container_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    docker_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    process_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    profile = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    notes = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    progress = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    capture_options = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "incident_evidence",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    incident_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    from_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    to_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    artifact_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    summary = table.Column<string>(type: "jsonb", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    selected_by_default = table.Column<bool>(type: "boolean", nullable: false),
                    sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    redaction_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_evidence", x => x.id);
                    table.ForeignKey(
                        name: "FK_incident_evidence_incidents_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "incident_findings",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    incident_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    confidence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_findings", x => x.id);
                    table.ForeignKey(
                        name: "FK_incident_findings_incidents_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "incident_timeline",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    incident_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    evidence_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_timeline", x => x.id);
                    table.ForeignKey(
                        name: "FK_incident_timeline_incidents_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "incident_finding_evidence",
                columns: table => new
                {
                    finding_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    evidence_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_finding_evidence", x => new { x.finding_id, x.evidence_id });
                    table.ForeignKey(
                        name: "FK_incident_finding_evidence_incident_evidence_evidence_id",
                        column: x => x.evidence_id,
                        principalTable: "incident_evidence",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_incident_finding_evidence_incident_findings_finding_id",
                        column: x => x.finding_id,
                        principalTable: "incident_findings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_incident_evidence_artifact_id",
                table: "incident_evidence",
                column: "artifact_id");

            migrationBuilder.CreateIndex(
                name: "IX_incident_evidence_incident_id_captured_at",
                table: "incident_evidence",
                columns: new[] { "incident_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "IX_incident_finding_evidence_evidence_id",
                table: "incident_finding_evidence",
                column: "evidence_id");

            migrationBuilder.CreateIndex(
                name: "IX_incident_findings_incident_id_created_at",
                table: "incident_findings",
                columns: new[] { "incident_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_incident_timeline_incident_id_timestamp",
                table: "incident_timeline",
                columns: new[] { "incident_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_created_at_status",
                table: "incidents",
                columns: new[] { "created_at", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_one_active_target",
                table: "incidents",
                column: "container_id",
                unique: true,
                filter: "status IN ('queued', 'collecting', 'analyzing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incident_finding_evidence");

            migrationBuilder.DropTable(
                name: "incident_timeline");

            migrationBuilder.DropTable(
                name: "incident_evidence");

            migrationBuilder.DropTable(
                name: "incident_findings");

            migrationBuilder.DropTable(
                name: "incidents");
        }
    }
}
