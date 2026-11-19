using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReopenApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reopen_requests",
                schema: "dc",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decision_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reopen_requests", x => x.id);
                    table.CheckConstraint("ck_reopen_requests_decision", "(status = 1 AND decided_at IS NULL) OR (status = 2 AND decided_at IS NOT NULL AND decision_ref IS NOT NULL) OR (status = 3 AND decided_at IS NOT NULL AND decision_note IS NOT NULL)");
                    table.CheckConstraint("ck_reopen_requests_sod", "decided_by IS NULL OR decided_by <> requested_by");
                    table.ForeignKey(
                        name: "fk_reopen_requests_submissions_submission_id",
                        column: x => x.submission_id,
                        principalSchema: "dc",
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "value_snapshots",
                schema: "dc",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    indicator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    numerator = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    denominator = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    edited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_value_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_value_snapshots_indicators_indicator_id",
                        column: x => x.indicator_id,
                        principalSchema: "kpi",
                        principalTable: "indicators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_value_snapshots_reopen_requests_request_id",
                        column: x => x.request_id,
                        principalSchema: "dc",
                        principalTable: "reopen_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_value_snapshots_submissions_submission_id",
                        column: x => x.submission_id,
                        principalSchema: "dc",
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reopen_requests_submission_id_status",
                schema: "dc",
                table: "reopen_requests",
                columns: new[] { "submission_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_reopen_requests_one_pending",
                schema: "dc",
                table: "reopen_requests",
                column: "submission_id",
                unique: true,
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_value_snapshots_indicator_id",
                schema: "dc",
                table: "value_snapshots",
                column: "indicator_id");

            migrationBuilder.CreateIndex(
                name: "ix_value_snapshots_request_id_indicator_id",
                schema: "dc",
                table: "value_snapshots",
                columns: new[] { "request_id", "indicator_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_value_snapshots_submission_id",
                schema: "dc",
                table: "value_snapshots",
                column: "submission_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "value_snapshots",
                schema: "dc");

            migrationBuilder.DropTable(
                name: "reopen_requests",
                schema: "dc");
        }
    }
}
