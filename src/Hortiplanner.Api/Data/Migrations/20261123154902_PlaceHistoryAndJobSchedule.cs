using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlaceHistoryAndJobSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "next_due_candidate",
                table: "jobs_done",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "scheduled_due",
                table: "jobs",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "location_changes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    previous_kind = table.Column<string>(type: "text", nullable: false),
                    previous_parent_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_location_changes_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_location_changes_locations_previous_parent_id",
                        column: x => x.previous_parent_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_location_changes_location_id_effective_at",
                table: "location_changes",
                columns: new[] { "location_id", "effective_at" });

            migrationBuilder.CreateIndex(
                name: "ix_location_changes_previous_parent_id",
                table: "location_changes",
                column: "previous_parent_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "location_changes");

            migrationBuilder.DropColumn(
                name: "next_due_candidate",
                table: "jobs_done");

            migrationBuilder.DropColumn(
                name: "scheduled_due",
                table: "jobs");
        }
    }
}
