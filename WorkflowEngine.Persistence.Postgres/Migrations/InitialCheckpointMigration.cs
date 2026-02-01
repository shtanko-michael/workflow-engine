using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkflowEngine.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialCheckpointMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "checkpoint_migrations",
                columns: table => new
                {
                    v = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checkpoint_migrations", x => x.v);
                });

            migrationBuilder.CreateTable(
                name: "checkpoints",
                columns: table => new
                {
                    thread_id = table.Column<string>(type: "text", nullable: false),
                    checkpoint_ns = table.Column<string>(type: "text", nullable: false),
                    checkpoint_id = table.Column<string>(type: "text", nullable: false),
                    parent_checkpoint_id = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<string>(type: "text", nullable: true),
                    checkpoint = table.Column<string>(type: "jsonb", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checkpoints", x => new { x.thread_id, x.checkpoint_ns, x.checkpoint_id });
                });

            migrationBuilder.CreateTable(
                name: "checkpoint_blobs",
                columns: table => new
                {
                    thread_id = table.Column<string>(type: "text", nullable: false),
                    checkpoint_ns = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    blob = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checkpoint_blobs", x => new { x.thread_id, x.checkpoint_ns, x.channel, x.version });
                });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checkpoint_migrations");

            migrationBuilder.DropTable(
                name: "checkpoints");

            migrationBuilder.DropTable(
                name: "checkpoint_blobs");

        }
    }
}
