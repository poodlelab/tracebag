using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tracebag.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContainerOperationalVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "container_targets",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    identity_source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    current_docker_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    compose_project = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    compose_service = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    compose_replica = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    image = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_container_targets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "docker_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    container_target_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    docker_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attributes = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_docker_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "container_instances",
                columns: table => new
                {
                    docker_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    container_target_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    image = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_container_instances", x => x.docker_id);
                    table.ForeignKey(
                        name: "FK_container_instances_container_targets_container_target_id",
                        column: x => x.container_target_id,
                        principalTable: "container_targets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_container_instances_container_target_id_created_at",
                table: "container_instances",
                columns: new[] { "container_target_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_container_targets_active",
                table: "container_targets",
                column: "active");

            migrationBuilder.CreateIndex(
                name: "IX_container_targets_current_docker_id",
                table: "container_targets",
                column: "current_docker_id");

            migrationBuilder.CreateIndex(
                name: "IX_docker_events_container_target_id_timestamp",
                table: "docker_events",
                columns: new[] { "container_target_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_docker_events_timestamp",
                table: "docker_events",
                column: "timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "container_instances");

            migrationBuilder.DropTable(
                name: "docker_events");

            migrationBuilder.DropTable(
                name: "container_targets");
        }
    }
}
