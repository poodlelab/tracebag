using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Tracebag.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProductionLogIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_log_entries_log_stream_id_received_at",
                table: "log_entries");

            migrationBuilder.AddColumn<bool>(
                name: "active",
                table: "log_streams",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "current_docker_id",
                table: "log_streams",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<long>(
                name: "max_bytes",
                table: "log_streams",
                type: "bigint",
                nullable: false,
                defaultValue: 268435456L);

            migrationBuilder.AddColumn<string>(
                name: "parser",
                table: "log_streams",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "auto");

            migrationBuilder.AddColumn<int>(
                name: "retention_days",
                table: "log_streams",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.Sql(
                "UPDATE log_entries SET timestamp = received_at WHERE timestamp IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "timestamp",
                table: "log_entries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "container_id",
                table: "log_entries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "docker_id",
                table: "log_entries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "fingerprint",
                table: "log_entries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "unmigrated");

            migrationBuilder.AddColumn<string>(
                name: "message",
                table: "log_entries",
                type: "text",
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "log_entries",
                type: "tsvector",
                nullable: false)
                .Annotation("Npgsql:TsVectorConfig", "simple")
                .Annotation("Npgsql:TsVectorProperties", new[] { "message", "line" });

            migrationBuilder.AddColumn<long>(
                name: "size_bytes",
                table: "log_entries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "source_timestamp",
                table: "log_entries",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "trace_id",
                table: "log_entries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "docker_id",
                table: "log_checkpoints",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "last_fingerprint",
                table: "log_checkpoints",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE log_streams
                SET current_docker_id = container_id,
                    parser = 'auto',
                    retention_days = 7,
                    max_bytes = 268435456,
                    active = true;

                UPDATE log_entries AS entry
                SET container_id = stream.container_id,
                    docker_id = stream.container_id,
                    message = entry.line,
                    source_timestamp = entry.timestamp::text,
                    fingerprint = md5(entry.id::text || ':' || coalesce(entry.line, '')),
                    size_bytes = octet_length(entry.line)
                FROM log_streams AS stream
                WHERE entry.log_stream_id = stream.id;

                UPDATE log_checkpoints
                SET docker_id = container_id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_log_streams_current_docker_id",
                table: "log_streams",
                column: "current_docker_id");

            migrationBuilder.CreateIndex(
                name: "IX_log_entries_container_id_timestamp_id",
                table: "log_entries",
                columns: new[] { "container_id", "timestamp", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_log_entries_fingerprint",
                table: "log_entries",
                column: "fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_log_entries_log_stream_id",
                table: "log_entries",
                column: "log_stream_id");

            migrationBuilder.CreateIndex(
                name: "IX_log_entries_search_vector",
                table: "log_entries",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_log_entries_trace_id",
                table: "log_entries",
                column: "trace_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_log_streams_current_docker_id",
                table: "log_streams");

            migrationBuilder.DropIndex(
                name: "IX_log_entries_container_id_timestamp_id",
                table: "log_entries");

            migrationBuilder.DropIndex(
                name: "IX_log_entries_fingerprint",
                table: "log_entries");

            migrationBuilder.DropIndex(
                name: "IX_log_entries_log_stream_id",
                table: "log_entries");

            migrationBuilder.DropIndex(
                name: "IX_log_entries_search_vector",
                table: "log_entries");

            migrationBuilder.DropIndex(
                name: "IX_log_entries_trace_id",
                table: "log_entries");

            migrationBuilder.DropColumn(
                name: "active",
                table: "log_streams");

            migrationBuilder.DropColumn(
                name: "current_docker_id",
                table: "log_streams");

            migrationBuilder.DropColumn(
                name: "max_bytes",
                table: "log_streams");

            migrationBuilder.DropColumn(
                name: "parser",
                table: "log_streams");

            migrationBuilder.DropColumn(
                name: "retention_days",
                table: "log_streams");

            migrationBuilder.DropColumn(
                name: "container_id",
                table: "log_entries");

            migrationBuilder.DropColumn(
                name: "docker_id",
                table: "log_entries");

            migrationBuilder.DropColumn(
                name: "fingerprint",
                table: "log_entries");

            migrationBuilder.DropColumn(
                name: "message",
                table: "log_entries");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "log_entries");

            migrationBuilder.DropColumn(
                name: "size_bytes",
                table: "log_entries");

            migrationBuilder.DropColumn(
                name: "source_timestamp",
                table: "log_entries");

            migrationBuilder.DropColumn(
                name: "trace_id",
                table: "log_entries");

            migrationBuilder.DropColumn(
                name: "docker_id",
                table: "log_checkpoints");

            migrationBuilder.DropColumn(
                name: "last_fingerprint",
                table: "log_checkpoints");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "timestamp",
                table: "log_entries",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.CreateIndex(
                name: "IX_log_entries_log_stream_id_received_at",
                table: "log_entries",
                columns: new[] { "log_stream_id", "received_at" });
        }
    }
}
