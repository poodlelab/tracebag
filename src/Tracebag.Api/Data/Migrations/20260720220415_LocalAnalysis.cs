using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracebag.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LocalAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "analysis_run_id",
                table: "incident_findings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "analysis_runs",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    incident_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    envelope_version = table.Column<int>(type: "integer", nullable: false),
                    analyzer_version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    envelope = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_analysis_runs_incidents_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_incident_findings_analysis_run_id",
                table: "incident_findings",
                column: "analysis_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_analysis_runs_incident_id_created_at",
                table: "analysis_runs",
                columns: new[] { "incident_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_analysis_runs_one_active_incident",
                table: "analysis_runs",
                column: "incident_id",
                unique: true,
                filter: "status = 'running'");

            migrationBuilder.AddForeignKey(
                name: "FK_incident_findings_analysis_runs_analysis_run_id",
                table: "incident_findings",
                column: "analysis_run_id",
                principalTable: "analysis_runs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_incident_findings_analysis_runs_analysis_run_id",
                table: "incident_findings");

            migrationBuilder.DropTable(
                name: "analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_incident_findings_analysis_run_id",
                table: "incident_findings");

            migrationBuilder.DropColumn(
                name: "analysis_run_id",
                table: "incident_findings");
        }
    }
}
