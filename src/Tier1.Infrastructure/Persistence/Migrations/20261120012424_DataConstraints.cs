using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DataConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "value",
                schema: "dc",
                table: "university_values",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "numerator",
                schema: "dc",
                table: "university_values",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "denominator",
                schema: "dc",
                table: "university_values",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "computed_value",
                schema: "dc",
                table: "university_values",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_university_values_ratio",
                schema: "dc",
                table: "university_values",
                sql: "denominator IS NULL OR denominator > 0");

            migrationBuilder.CreateIndex(
                name: "ux_cycles_official_quarter",
                schema: "dc",
                table: "cycles",
                columns: new[] { "target_year", "quarter" },
                unique: true,
                filter: "NOT trial AND quarter IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cycles_quarter",
                schema: "dc",
                table: "cycles",
                sql: "quarter IS NULL OR quarter BETWEEN 1 AND 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_university_values_ratio",
                schema: "dc",
                table: "university_values");

            migrationBuilder.DropIndex(
                name: "ux_cycles_official_quarter",
                schema: "dc",
                table: "cycles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cycles_quarter",
                schema: "dc",
                table: "cycles");

            migrationBuilder.AlterColumn<decimal>(
                name: "value",
                schema: "dc",
                table: "university_values",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,4)",
                oldPrecision: 20,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "numerator",
                schema: "dc",
                table: "university_values",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,4)",
                oldPrecision: 20,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "denominator",
                schema: "dc",
                table: "university_values",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,4)",
                oldPrecision: 20,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "computed_value",
                schema: "dc",
                table: "university_values",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,4)",
                oldPrecision: 20,
                oldScale: 4,
                oldNullable: true);
        }
    }
}
