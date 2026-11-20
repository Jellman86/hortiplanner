using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BatchCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "batch_costs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    answer = table.Column<string>(type: "text", nullable: false),
                    amount_paid = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    includes_vat = table.Column<bool>(type: "boolean", nullable: true),
                    vat_band = table.Column<string>(type: "text", nullable: true),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    vat_said = table.Column<bool>(type: "boolean", nullable: false),
                    net_cost = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    vat = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    replaces_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    request_fingerprint = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_costs", x => x.id);
                    table.CheckConstraint("ck_batch_costs_amount", "amount_paid IS NULL OR (amount_paid > 0 AND amount_paid < 100000)");
                    table.CheckConstraint("ck_batch_costs_vat_rate", "vat_rate IS NULL OR (vat_rate >= 0 AND vat_rate <= 100)");
                    table.ForeignKey(
                        name: "fk_batch_costs_asp_net_users_recorded_by",
                        column: x => x.recorded_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_costs_batch_costs_replaces_id",
                        column: x => x.replaces_id,
                        principalTable: "batch_costs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_costs_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_batch_costs_batch_id",
                table: "batch_costs",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_costs_recorded_by",
                table: "batch_costs",
                column: "recorded_by");

            migrationBuilder.CreateIndex(
                name: "ix_batch_costs_replaces_id",
                table: "batch_costs",
                column: "replaces_id",
                unique: true);

            // A cost once said is kept as it was: a correction is a new entry that replaces it.
            migrationBuilder.Sql("""
                CREATE TRIGGER batch_costs_refuse_update_delete
                    BEFORE UPDATE OR DELETE ON batch_costs
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER batch_costs_refuse_truncate
                    BEFORE TRUNCATE ON batch_costs
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "batch_costs");
        }
    }
}
