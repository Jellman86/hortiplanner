using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FamilyTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lineage_checked_at",
                table: "species",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "taxon_id",
                table: "species",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "taxa",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rank = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scientific_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    common_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    common_name_source = table.Column<string>(type: "text", nullable: true),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gbif_key = table.Column<long>(type: "bigint", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    licence = table.Column<string>(type: "text", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_taxa", x => x.id);
                    table.ForeignKey(
                        name: "fk_taxa_taxa_parent_id",
                        column: x => x.parent_id,
                        principalTable: "taxa",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_species_taxon_id",
                table: "species",
                column: "taxon_id");

            migrationBuilder.CreateIndex(
                name: "ix_taxa_gbif_key",
                table: "taxa",
                column: "gbif_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_taxa_parent_id",
                table: "taxa",
                column: "parent_id");

            migrationBuilder.AddForeignKey(
                name: "fk_species_taxa_taxon_id",
                table: "species",
                column: "taxon_id",
                principalTable: "taxa",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_species_taxa_taxon_id",
                table: "species");

            migrationBuilder.DropTable(
                name: "taxa");

            migrationBuilder.DropIndex(
                name: "ix_species_taxon_id",
                table: "species");

            migrationBuilder.DropColumn(
                name: "lineage_checked_at",
                table: "species");

            migrationBuilder.DropColumn(
                name: "taxon_id",
                table: "species");
        }
    }
}
