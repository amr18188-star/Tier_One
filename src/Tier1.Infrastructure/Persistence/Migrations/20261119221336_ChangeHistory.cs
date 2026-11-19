using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_target_revisions_targets_target_id",
                schema: "kpi",
                table: "target_revisions");

            migrationBuilder.AlterColumn<Guid>(
                name: "target_id",
                schema: "kpi",
                table: "target_revisions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<int>(
                name: "action",
                schema: "kpi",
                table: "target_revisions",
                type: "integer",
                nullable: false,
                defaultValue: 1); // السطور القائمة كلها تعديلات مستهدفات معتمدة (TargetRevisionAction.Revised)

            migrationBuilder.AddColumn<string>(
                name: "changed_by_name",
                schema: "kpi",
                table: "target_revisions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "indicator_id",
                schema: "kpi",
                table: "target_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "kind",
                schema: "kpi",
                table: "target_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "unit_id",
                schema: "kpi",
                table: "target_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "year",
                schema: "kpi",
                table: "target_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "university_value_changes",
                schema: "dc",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    indicator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    quarter = table.Column<int>(type: "integer", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_value = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    new_value = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    computed_value = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    reported = table.Column<int>(type: "integer", nullable: false),
                    expected = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_university_value_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_university_value_changes_cycles_cycle_id",
                        column: x => x.cycle_id,
                        principalSchema: "dc",
                        principalTable: "cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_university_value_changes_indicators_indicator_id",
                        column: x => x.indicator_id,
                        principalSchema: "kpi",
                        principalTable: "indicators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // D8: ملء ما سبق. سطور سجل المستهدفات تصف نفسها من مستهدفها ومن عدلها،
            // وكل قيمة رسمية قائمة للجامعة تبدأ تاريخها بسطر نقلها الحالي (لا قيمة قبلها معروفة في القاعدة).
            migrationBuilder.Sql("""
                UPDATE kpi.target_revisions r
                   SET indicator_id = t.indicator_id, unit_id = t.unit_id, year = t.year, kind = t.kind
                  FROM kpi.targets t
                 WHERE t.id = r.target_id;
                UPDATE kpi.target_revisions r
                   SET changed_by_name = u.full_name_ar
                  FROM iam.users u
                 WHERE u.id = r.changed_by;
                INSERT INTO dc.university_value_changes
                       (indicator_id, year, quarter, cycle_id, old_value, new_value, computed_value, reported, expected, reason, changed_by, changed_by_name, changed_at)
                SELECT indicator_id, year, quarter, cycle_id, NULL, value, computed_value, reported, expected, note, transferred_by, transferred_by_name, transferred_at
                  FROM dc.university_values;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_target_revisions_indicator_id_changed_at",
                schema: "kpi",
                table: "target_revisions",
                columns: new[] { "indicator_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_university_value_changes_cycle_id",
                schema: "dc",
                table: "university_value_changes",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_university_value_changes_indicator_id_year_quarter_changed_",
                schema: "dc",
                table: "university_value_changes",
                columns: new[] { "indicator_id", "year", "quarter", "changed_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_target_revisions_indicators_indicator_id",
                schema: "kpi",
                table: "target_revisions",
                column: "indicator_id",
                principalSchema: "kpi",
                principalTable: "indicators",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_target_revisions_targets_target_id",
                schema: "kpi",
                table: "target_revisions",
                column: "target_id",
                principalSchema: "kpi",
                principalTable: "targets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_target_revisions_indicators_indicator_id",
                schema: "kpi",
                table: "target_revisions");

            migrationBuilder.DropForeignKey(
                name: "fk_target_revisions_targets_target_id",
                schema: "kpi",
                table: "target_revisions");

            migrationBuilder.DropTable(
                name: "university_value_changes",
                schema: "dc");

            migrationBuilder.DropIndex(
                name: "ix_target_revisions_indicator_id_changed_at",
                schema: "kpi",
                table: "target_revisions");

            migrationBuilder.DropColumn(
                name: "action",
                schema: "kpi",
                table: "target_revisions");

            migrationBuilder.DropColumn(
                name: "changed_by_name",
                schema: "kpi",
                table: "target_revisions");

            migrationBuilder.DropColumn(
                name: "indicator_id",
                schema: "kpi",
                table: "target_revisions");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "kpi",
                table: "target_revisions");

            migrationBuilder.DropColumn(
                name: "unit_id",
                schema: "kpi",
                table: "target_revisions");

            migrationBuilder.DropColumn(
                name: "year",
                schema: "kpi",
                table: "target_revisions");

            migrationBuilder.AlterColumn<Guid>(
                name: "target_id",
                schema: "kpi",
                table: "target_revisions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_target_revisions_targets_target_id",
                schema: "kpi",
                table: "target_revisions",
                column: "target_id",
                principalSchema: "kpi",
                principalTable: "targets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
