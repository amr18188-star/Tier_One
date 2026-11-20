using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DomainEventOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "int");

            migrationBuilder.CreateTable(
                name: "outbox_events",
                schema: "int",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    aggregate_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                schema: "int",
                columns: table => new
                {
                    handler = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_events", x => new { x.handler, x.event_id });
                    table.ForeignKey(
                        name: "fk_processed_events_outbox_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "int",
                        principalTable: "outbox_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_aggregate",
                schema: "int",
                table: "outbox_events",
                columns: new[] { "aggregate_type", "aggregate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_pending",
                schema: "int",
                table: "outbox_events",
                column: "available_at",
                filter: "processed_at IS NULL AND failed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_processed_at",
                schema: "int",
                table: "outbox_events",
                column: "processed_at",
                filter: "processed_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_processed_events_event_id",
                schema: "int",
                table: "processed_events",
                column: "event_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_events",
                schema: "int");

            migrationBuilder.DropTable(
                name: "outbox_events",
                schema: "int");
        }
    }
}
