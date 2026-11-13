using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnitIndicators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "custom_indicators",
                schema: "org",
                table: "units",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "views_whole_university",
                schema: "org",
                table: "units",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "unit_ids",
                schema: "dc",
                table: "cycle_indicators",
                type: "uuid[]",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "unit_indicators",
                schema: "org",
                columns: table => new
                {
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    indicator_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_indicators", x => new { x.unit_id, x.indicator_id });
                    table.ForeignKey(
                        name: "fk_unit_indicators_indicators_indicator_id",
                        column: x => x.indicator_id,
                        principalSchema: "kpi",
                        principalTable: "indicators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_unit_indicators_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "org",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_unit_indicators_indicator_id",
                schema: "org",
                table: "unit_indicators",
                column: "indicator_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "unit_indicators",
                schema: "org");

            migrationBuilder.DropColumn(
                name: "custom_indicators",
                schema: "org",
                table: "units");

            migrationBuilder.DropColumn(
                name: "views_whole_university",
                schema: "org",
                table: "units");

            migrationBuilder.DropColumn(
                name: "unit_ids",
                schema: "dc",
                table: "cycle_indicators");
        }
    }
}
