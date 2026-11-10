using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Collection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dc");

            migrationBuilder.CreateTable(
                name: "cycles",
                schema: "dc",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    period_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    target_year = table.Column<int>(type: "integer", nullable: false),
                    opens_on = table.Column<DateOnly>(type: "date", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    trial = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cycles", x => x.id);
                    table.CheckConstraint("ck_cycles_dates", "due_on >= opens_on");
                });

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "dc",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cycle_indicators",
                schema: "dc",
                columns: table => new
                {
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    indicator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    central_unit_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cycle_indicators", x => new { x.cycle_id, x.indicator_id });
                    table.ForeignKey(
                        name: "fk_cycle_indicators_cycles_cycle_id",
                        column: x => x.cycle_id,
                        principalSchema: "dc",
                        principalTable: "cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cycle_indicators_indicator_versions_version_id",
                        column: x => x.version_id,
                        principalSchema: "kpi",
                        principalTable: "indicator_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cycle_indicators_indicators_indicator_id",
                        column: x => x.indicator_id,
                        principalSchema: "kpi",
                        principalTable: "indicators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                schema: "dc",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    last_edited_by = table.Column<Guid>(type: "uuid", nullable: true),
                    last_edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    return_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_submissions_cycles_cycle_id",
                        column: x => x.cycle_id,
                        principalSchema: "dc",
                        principalTable: "cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submissions_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "org",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submission_events",
                schema: "dc",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    from_step = table.Column<int>(type: "integer", nullable: false),
                    to_step = table.Column<int>(type: "integer", nullable: false),
                    by = table.Column<Guid>(type: "uuid", nullable: false),
                    by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_submission_events_submissions_submission_id",
                        column: x => x.submission_id,
                        principalSchema: "dc",
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "values",
                schema: "dc",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    indicator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    numerator = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    denominator = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    review_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    edited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_values", x => x.id);
                    table.CheckConstraint("ck_values_kind", "(kind = 1) = (value IS NOT NULL) AND (kind = 1 OR reason IS NOT NULL)");
                    table.CheckConstraint("ck_values_ratio", "denominator IS NULL OR denominator > 0");
                    table.ForeignKey(
                        name: "fk_values_indicators_indicator_id",
                        column: x => x.indicator_id,
                        principalSchema: "kpi",
                        principalTable: "indicators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_values_submissions_submission_id",
                        column: x => x.submission_id,
                        principalSchema: "dc",
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cycle_indicators_indicator_id",
                schema: "dc",
                table: "cycle_indicators",
                column: "indicator_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_indicators_version_id",
                schema: "dc",
                table: "cycle_indicators",
                column: "version_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_owner_type_owner_id",
                schema: "dc",
                table: "documents",
                columns: new[] { "owner_type", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_submission_events_submission_id",
                schema: "dc",
                table: "submission_events",
                column: "submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_submissions_cycle_id_status",
                schema: "dc",
                table: "submissions",
                columns: new[] { "cycle_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_submissions_cycle_id_unit_id",
                schema: "dc",
                table: "submissions",
                columns: new[] { "cycle_id", "unit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_submissions_unit_id",
                schema: "dc",
                table: "submissions",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_values_indicator_id",
                schema: "dc",
                table: "values",
                column: "indicator_id");

            migrationBuilder.CreateIndex(
                name: "ix_values_submission_id_indicator_id",
                schema: "dc",
                table: "values",
                columns: new[] { "submission_id", "indicator_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cycle_indicators",
                schema: "dc");

            migrationBuilder.DropTable(
                name: "documents",
                schema: "dc");

            migrationBuilder.DropTable(
                name: "submission_events",
                schema: "dc");

            migrationBuilder.DropTable(
                name: "values",
                schema: "dc");

            migrationBuilder.DropTable(
                name: "submissions",
                schema: "dc");

            migrationBuilder.DropTable(
                name: "cycles",
                schema: "dc");
        }
    }
}
