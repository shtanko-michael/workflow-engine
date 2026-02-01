using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkflowEngine.Tests.UI.Backend.Migrations
{
    public partial class ConversationsAndMessages : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_dialogs_message_versions_active_root_version_id",
                table: "dialogs");

            migrationBuilder.DropTable(name: "message_versions");
            migrationBuilder.DropTable(name: "dialogs");

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    thread_id = table.Column<string>(type: "text", nullable: true),
                    last_checkpoint_id = table.Column<string>(type: "text", nullable: true),
                    last_interrupt_request_id = table.Column<string>(type: "text", nullable: true),
                    active_leaf_message_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    conversation_id = table.Column<string>(type: "text", nullable: false),
                    parent_id = table.Column<string>(type: "text", nullable: true),
                    role = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    checkpoint_id = table.Column<string>(type: "text", nullable: true),
                    request_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_messages_messages_parent_id",
                        column: x => x.parent_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_active_leaf_message_id",
                table: "conversations",
                column: "active_leaf_message_id");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_created_at",
                table: "conversations",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_updated_at",
                table: "conversations",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "IX_messages_conversation_id",
                table: "messages",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_parent_id",
                table: "messages",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_created_at",
                table: "messages",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_messages_conversation_id_parent_id",
                table: "messages",
                columns: new[] { "conversation_id", "parent_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_messages_active_leaf_message_id",
                table: "conversations",
                column: "active_leaf_message_id",
                principalTable: "messages",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversations_messages_active_leaf_message_id",
                table: "conversations");

            migrationBuilder.DropTable(name: "messages");
            migrationBuilder.DropTable(name: "conversations");

            migrationBuilder.CreateTable(
                name: "dialogs",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    thread_id = table.Column<string>(type: "text", nullable: false),
                    workflow_type = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    last_checkpoint_id = table.Column<string>(type: "text", nullable: true),
                    last_interrupt_request_id = table.Column<string>(type: "text", nullable: true),
                    active_root_version_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dialogs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "message_versions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    dialog_id = table.Column<string>(type: "text", nullable: false),
                    parent_version_id = table.Column<string>(type: "text", nullable: true),
                    path = table.Column<string>(type: "text", nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    checkpoint_id = table.Column<string>(type: "text", nullable: false),
                    request_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_message_versions_dialogs_dialog_id",
                        column: x => x.dialog_id,
                        principalTable: "dialogs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_message_versions_message_versions_parent_version_id",
                        column: x => x.parent_version_id,
                        principalTable: "message_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dialogs_active_root_version_id",
                table: "dialogs",
                column: "active_root_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_dialogs_created_at",
                table: "dialogs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_dialogs_thread_id",
                table: "dialogs",
                column: "thread_id");

            migrationBuilder.CreateIndex(
                name: "IX_message_versions_dialog_id",
                table: "message_versions",
                column: "dialog_id");

            migrationBuilder.CreateIndex(
                name: "IX_message_versions_dialog_id_depth",
                table: "message_versions",
                columns: new[] { "dialog_id", "depth" });

            migrationBuilder.CreateIndex(
                name: "IX_message_versions_dialog_id_path",
                table: "message_versions",
                columns: new[] { "dialog_id", "path" });

            migrationBuilder.CreateIndex(
                name: "IX_message_versions_parent_version_id",
                table: "message_versions",
                column: "parent_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_message_versions_path",
                table: "message_versions",
                column: "path");

            migrationBuilder.AddForeignKey(
                name: "FK_dialogs_message_versions_active_root_version_id",
                table: "dialogs",
                column: "active_root_version_id",
                principalTable: "message_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
