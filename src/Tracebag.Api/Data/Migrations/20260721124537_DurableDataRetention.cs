using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracebag.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DurableDataRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_incident_evidence_kind_source_id",
                table: "incident_evidence",
                columns: new[] { "kind", "source_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_incident_evidence_kind_source_id",
                table: "incident_evidence");
        }
    }
}
