using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitiativeActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "initiative_activities",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    initiative_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    owner_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    progress = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_initiative_activities", x => x.id);
                    table.CheckConstraint("ck_activities_dates", "end_date IS NULL OR start_date IS NULL OR end_date >= start_date");
                    table.CheckConstraint("ck_activities_progress", "progress BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_initiative_activities_initiatives_initiative_id",
                        column: x => x.initiative_id,
                        principalSchema: "kpi",
                        principalTable: "initiatives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_initiative_activities_units_owner_unit_id",
                        column: x => x.owner_unit_id,
                        principalSchema: "org",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_initiative_activities_initiative_id_sort_order",
                schema: "kpi",
                table: "initiative_activities",
                columns: new[] { "initiative_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_initiative_activities_owner_unit_id",
                schema: "kpi",
                table: "initiative_activities",
                column: "owner_unit_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "initiative_activities",
                schema: "kpi");
        }
    }
}
