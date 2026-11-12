using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StrategyPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "kpi",
                table: "standards",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name_en",
                schema: "kpi",
                table: "standards",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "kpi",
                table: "standards",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "kpi",
                table: "pillars",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name_en",
                schema: "kpi",
                table: "pillars",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "kpi",
                table: "pillars",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<int>(
                name: "plan_end_year",
                schema: "kpi",
                table: "indicators",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan_note",
                schema: "kpi",
                table: "indicators",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "plan_start_year",
                schema: "kpi",
                table: "indicators",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "college_entry",
                schema: "dc",
                table: "cycle_indicators",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "initiatives",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    standard_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    expected_outcome = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    owner_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_year = table.Column<int>(type: "integer", nullable: false),
                    end_year = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    indicator_codes = table.Column<List<string>>(type: "text[]", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_initiatives", x => x.id);
                    table.CheckConstraint("ck_initiatives_years", "end_year >= start_year");
                    table.ForeignKey(
                        name: "fk_initiatives_standards_standard_id",
                        column: x => x.standard_id,
                        principalSchema: "kpi",
                        principalTable: "standards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_initiatives_units_owner_unit_id",
                        column: x => x.owner_unit_id,
                        principalSchema: "org",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_initiatives_code",
                schema: "kpi",
                table: "initiatives",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_initiatives_owner_unit_id",
                schema: "kpi",
                table: "initiatives",
                column: "owner_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_initiatives_standard_id",
                schema: "kpi",
                table: "initiatives",
                column: "standard_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "initiatives",
                schema: "kpi");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "kpi",
                table: "standards");

            migrationBuilder.DropColumn(
                name: "name_en",
                schema: "kpi",
                table: "standards");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "kpi",
                table: "standards");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "kpi",
                table: "pillars");

            migrationBuilder.DropColumn(
                name: "name_en",
                schema: "kpi",
                table: "pillars");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "kpi",
                table: "pillars");

            migrationBuilder.DropColumn(
                name: "plan_end_year",
                schema: "kpi",
                table: "indicators");

            migrationBuilder.DropColumn(
                name: "plan_note",
                schema: "kpi",
                table: "indicators");

            migrationBuilder.DropColumn(
                name: "plan_start_year",
                schema: "kpi",
                table: "indicators");

            migrationBuilder.DropColumn(
                name: "college_entry",
                schema: "dc",
                table: "cycle_indicators");
        }
    }
}
