using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class VatRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vat_rate_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    band = table.Column<string>(type: "text", nullable: false),
                    percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    set_by = table.Column<Guid>(type: "uuid", nullable: true),
                    set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vat_rate_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_vat_rate_changes_asp_net_users_set_by",
                        column: x => x.set_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vat_rate_changes_band_from",
                table: "vat_rate_changes",
                columns: new[] { "band", "from" });

            migrationBuilder.CreateIndex(
                name: "ix_vat_rate_changes_set_by",
                table: "vat_rate_changes",
                column: "set_by");

            // The starting rates, from the earliest day, set by nobody.
            migrationBuilder.Sql("""
                INSERT INTO vat_rate_changes (id, band, percent, "from", set_by, set_at) VALUES
                    ('0193a6f0-0000-7000-8000-00000000a7e1', 'Standard', 20, DATE '2000-01-01', NULL, now()),
                    ('0193a6f0-0000-7000-8000-00000000a7e2', 'Reduced', 5, DATE '2000-01-01', NULL, now());
                """);

            // A rate once set is part of the record of how invoices and costs were worked out, so it is never changed.
            migrationBuilder.Sql("""
                CREATE TRIGGER vat_rate_changes_refuse_update_delete
                    BEFORE UPDATE OR DELETE ON vat_rate_changes
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER vat_rate_changes_refuse_truncate
                    BEFORE TRUNCATE ON vat_rate_changes
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vat_rate_changes");
        }
    }
}
