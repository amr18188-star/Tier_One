using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KpiFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<int>>(
                name: "perspectives",
                schema: "kpi",
                table: "indicators",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "'{}'::integer[]");

            migrationBuilder.AddColumn<string>(
                name: "pss_rationale",
                schema: "kpi",
                table: "indicators",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sdg_induced",
                schema: "kpi",
                table: "indicators",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sdg_primary",
                schema: "kpi",
                table: "indicators",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sdg_rationale",
                schema: "kpi",
                table: "indicators",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sdg_secondary",
                schema: "kpi",
                table: "indicators",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "accumulation",
                schema: "kpi",
                table: "indicator_versions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "formula",
                schema: "kpi",
                table: "indicator_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "quarter",
                schema: "dc",
                table: "cycles",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "perspectives",
                schema: "kpi",
                table: "indicators");

            migrationBuilder.DropColumn(
                name: "pss_rationale",
                schema: "kpi",
                table: "indicators");

            migrationBuilder.DropColumn(
                name: "sdg_induced",
                schema: "kpi",
                table: "indicators");

            migrationBuilder.DropColumn(
                name: "sdg_primary",
                schema: "kpi",
                table: "indicators");

            migrationBuilder.DropColumn(
                name: "sdg_rationale",
                schema: "kpi",
                table: "indicators");

            migrationBuilder.DropColumn(
                name: "sdg_secondary",
                schema: "kpi",
                table: "indicators");

            migrationBuilder.DropColumn(
                name: "accumulation",
                schema: "kpi",
                table: "indicator_versions");

            migrationBuilder.DropColumn(
                name: "formula",
                schema: "kpi",
                table: "indicator_versions");

            migrationBuilder.DropColumn(
                name: "quarter",
                schema: "dc",
                table: "cycles");
        }
    }
}
