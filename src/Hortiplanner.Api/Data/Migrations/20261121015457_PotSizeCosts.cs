using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PotSizeCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pot_size_costs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pot_size_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_each = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    includes_vat = table.Column<bool>(type: "boolean", nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    set_by = table.Column<Guid>(type: "uuid", nullable: false),
                    set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pot_size_costs", x => x.id);
                    table.CheckConstraint("ck_pot_size_costs_cost_each", "cost_each IS NULL OR (cost_each > 0 AND cost_each < 1000)");
                    table.CheckConstraint("ck_pot_size_costs_vat_rate", "vat_rate >= 0 AND vat_rate <= 100");
                    table.ForeignKey(
                        name: "fk_pot_size_costs_asp_net_users_set_by",
                        column: x => x.set_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pot_size_costs_pot_sizes_pot_size_id",
                        column: x => x.pot_size_id,
                        principalTable: "pot_sizes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pot_size_costs_pot_size_id_from",
                table: "pot_size_costs",
                columns: new[] { "pot_size_id", "from" });

            migrationBuilder.CreateIndex(
                name: "ix_pot_size_costs_set_by",
                table: "pot_size_costs",
                column: "set_by");

            // An allowance once set is part of how earlier potting was costed, so it is never changed.
            migrationBuilder.Sql("""
                CREATE TRIGGER pot_size_costs_refuse_update_delete
                    BEFORE UPDATE OR DELETE ON pot_size_costs
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER pot_size_costs_refuse_truncate
                    BEFORE TRUNCATE ON pot_size_costs
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pot_size_costs");
        }
    }
}
