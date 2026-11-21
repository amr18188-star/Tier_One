using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniversityAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ip_restricted",
                schema: "iam",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "global_ip_restricted",
                schema: "iam",
                table: "access_state",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "email_domains",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    university_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    university_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_domains", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ip_allow_rules",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ip_allow_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_ip_allow_rules_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                schema: "iam",
                table: "access_state",
                keyColumn: "id",
                keyValue: 1,
                column: "global_ip_restricted",
                value: false);

            migrationBuilder.CreateIndex(
                name: "ix_email_domains_domain",
                schema: "iam",
                table: "email_domains",
                column: "domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ip_allow_rules_user_id_cidr",
                schema: "iam",
                table: "ip_allow_rules",
                columns: new[] { "user_id", "cidr" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            // ADR-0016: the first approved universities are data, inserted once here; the system administrator manages them from then on
            // (Settings > Allowed university domains). The application code holds no domain.
            migrationBuilder.Sql("""
                INSERT INTO iam.email_domains (id, domain, university_ar, university_en, active, created_at)
                VALUES ('019aa1c0-0000-7000-8000-000000000001', 'capu.edu.eg', 'جامعة العاصمة', 'Capital University', true, now()),
                       ('019aa1c0-0000-7000-8000-000000000002', 'helwan.edu.eg', 'جامعة حلوان', 'Helwan University', true, now())
                ON CONFLICT (domain) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_domains",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "ip_allow_rules",
                schema: "iam");

            migrationBuilder.DropColumn(
                name: "ip_restricted",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "global_ip_restricted",
                schema: "iam",
                table: "access_state");
        }
    }
}
