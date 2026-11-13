using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniversityValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "university_values",
                schema: "dc",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    indicator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    quarter = table.Column<int>(type: "integer", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<decimal>(type: "numeric", nullable: true),
                    numerator = table.Column<decimal>(type: "numeric", nullable: true),
                    denominator = table.Column<decimal>(type: "numeric", nullable: true),
                    computed_value = table.Column<decimal>(type: "numeric", nullable: true),
                    reported = table.Column<int>(type: "integer", nullable: false),
                    expected = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    transferred_by = table.Column<Guid>(type: "uuid", nullable: false),
                    transferred_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    transferred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_university_values", x => x.id);
                    table.CheckConstraint("ck_university_values_quarter", "quarter BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_university_values_cycles_cycle_id",
                        column: x => x.cycle_id,
                        principalSchema: "dc",
                        principalTable: "cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_university_values_indicators_indicator_id",
                        column: x => x.indicator_id,
                        principalSchema: "kpi",
                        principalTable: "indicators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_university_values_cycle_id",
                schema: "dc",
                table: "university_values",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_university_values_indicator_id_year_quarter",
                schema: "dc",
                table: "university_values",
                columns: new[] { "indicator_id", "year", "quarter" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "university_values",
                schema: "dc");
        }
    }
}
