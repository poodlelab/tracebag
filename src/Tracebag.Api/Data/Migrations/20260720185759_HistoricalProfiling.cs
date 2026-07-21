using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracebag.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class HistoricalProfiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rows created before durable recording sessions have no recording
            // identifier and therefore cannot be migrated safely.
            migrationBuilder.Sql("TRUNCATE TABLE counter_rollups_1m;");

            migrationBuilder.DropIndex(
                name: "IX_counter_recording_sessions_container_id",
                table: "counter_recording_sessions");

            migrationBuilder.AddColumn<string>(
                name: "session_id",
                table: "counter_rollups_1m",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "counter_recording_sessions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "runner_image",
                table: "counter_recording_sessions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "tracebag-runner-dotnet-8:dev");

            migrationBuilder.AddColumn<int>(
                name: "runtime_major",
                table: "counter_recording_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 8);

            migrationBuilder.AddColumn<string>(
                name: "tool_version",
                table: "counter_recording_sessions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "8.0.547301");

            migrationBuilder.CreateIndex(
                name: "IX_counter_rollups_1m_session_id_provider_name_counter_type_bu~",
                table: "counter_rollups_1m",
                columns: new[] { "session_id", "provider", "name", "counter_type", "bucket_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_counter_recording_sessions_one_active_target",
                table: "counter_recording_sessions",
                column: "container_id",
                unique: true,
                filter: "status IN ('starting', 'running', 'stopping')");

            migrationBuilder.AddForeignKey(
                name: "FK_counter_rollups_1m_counter_recording_sessions_session_id",
                table: "counter_rollups_1m",
                column: "session_id",
                principalTable: "counter_recording_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_counter_rollups_1m_counter_recording_sessions_session_id",
                table: "counter_rollups_1m");

            migrationBuilder.DropIndex(
                name: "IX_counter_rollups_1m_session_id_provider_name_counter_type_bu~",
                table: "counter_rollups_1m");

            migrationBuilder.DropIndex(
                name: "IX_counter_recording_sessions_one_active_target",
                table: "counter_recording_sessions");

            migrationBuilder.DropColumn(
                name: "session_id",
                table: "counter_rollups_1m");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "counter_recording_sessions");

            migrationBuilder.DropColumn(
                name: "runner_image",
                table: "counter_recording_sessions");

            migrationBuilder.DropColumn(
                name: "runtime_major",
                table: "counter_recording_sessions");

            migrationBuilder.DropColumn(
                name: "tool_version",
                table: "counter_recording_sessions");

            migrationBuilder.CreateIndex(
                name: "IX_counter_recording_sessions_container_id",
                table: "counter_recording_sessions",
                column: "container_id");
        }
    }
}
