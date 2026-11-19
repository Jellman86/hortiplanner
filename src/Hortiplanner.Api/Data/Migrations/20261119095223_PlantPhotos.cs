using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlantPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_plant_photos",
                table: "plant_photos");

            migrationBuilder.AddColumn<Guid>(
                name: "id",
                table: "plant_photos",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<string>(
                name: "caption",
                table: "plant_photos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "plant_photos",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<bool>(
                name: "is_main",
                table: "plant_photos",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "plant_photos",
                type: "text",
                nullable: false,
                defaultValue: "Other");

            migrationBuilder.AddColumn<Guid>(
                name: "uploaded_by",
                table: "plant_photos",
                type: "uuid",
                nullable: true);

            // Each plant's single picture became its main photo, with an id of its own. New photos are given their id
            // and kind by the app, and aren't the main one unless it says so.
            migrationBuilder.Sql("ALTER TABLE plant_photos ALTER COLUMN id DROP DEFAULT, ALTER COLUMN is_main DROP DEFAULT, ALTER COLUMN kind DROP DEFAULT;");

            migrationBuilder.AddPrimaryKey(
                name: "pk_plant_photos",
                table: "plant_photos",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_plant_photos_one_main",
                table: "plant_photos",
                columns: new[] { "plant_id", "is_main" },
                unique: true,
                filter: "is_main");

            migrationBuilder.CreateIndex(
                name: "ix_plant_photos_plant_id",
                table: "plant_photos",
                column: "plant_id");

            migrationBuilder.CreateIndex(
                name: "ix_plant_photos_uploaded_by",
                table: "plant_photos",
                column: "uploaded_by");

            migrationBuilder.AddForeignKey(
                name: "fk_plant_photos_asp_net_users_uploaded_by",
                table: "plant_photos",
                column: "uploaded_by",
                principalTable: "AspNetUsers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Before photos were kept in numbers a plant had one picture: its main photo is the one kept.
            migrationBuilder.Sql("DELETE FROM plant_photos WHERE NOT is_main;");

            migrationBuilder.DropForeignKey(
                name: "fk_plant_photos_asp_net_users_uploaded_by",
                table: "plant_photos");

            migrationBuilder.DropPrimaryKey(
                name: "pk_plant_photos",
                table: "plant_photos");

            migrationBuilder.DropIndex(
                name: "ix_plant_photos_one_main",
                table: "plant_photos");

            migrationBuilder.DropIndex(
                name: "ix_plant_photos_plant_id",
                table: "plant_photos");

            migrationBuilder.DropIndex(
                name: "ix_plant_photos_uploaded_by",
                table: "plant_photos");

            migrationBuilder.DropColumn(
                name: "id",
                table: "plant_photos");

            migrationBuilder.DropColumn(
                name: "caption",
                table: "plant_photos");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "plant_photos");

            migrationBuilder.DropColumn(
                name: "is_main",
                table: "plant_photos");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "plant_photos");

            migrationBuilder.DropColumn(
                name: "uploaded_by",
                table: "plant_photos");

            migrationBuilder.AddPrimaryKey(
                name: "pk_plant_photos",
                table: "plant_photos",
                column: "plant_id");
        }
    }
}
