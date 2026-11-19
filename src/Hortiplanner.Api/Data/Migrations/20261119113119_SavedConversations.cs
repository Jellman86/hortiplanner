using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SavedConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistant_conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    about_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    about_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_conversations", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_conversations_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assistant_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    text = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    photo_count = table.Column<int>(type: "integer", nullable: false),
                    sources = table.Column<string>(type: "jsonb", nullable: true),
                    cost = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_messages_assistant_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "assistant_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_conversations_owner_id_updated_at",
                table: "assistant_conversations",
                columns: new[] { "owner_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_messages_conversation_id_created_at",
                table: "assistant_messages",
                columns: new[] { "conversation_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_messages");

            migrationBuilder.DropTable(
                name: "assistant_conversations");
        }
    }
}
