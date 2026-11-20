using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SharedLoginAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "login_attempts",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    surface = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "login_counters",
                schema: "iam",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    failures = table.Column<int>(type: "integer", nullable: false),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    blocked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_counters", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_login_attempts_at",
                schema: "iam",
                table: "login_attempts",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_login_attempts_ip_at",
                schema: "iam",
                table: "login_attempts",
                columns: new[] { "ip", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_login_attempts_user_id_ip_at",
                schema: "iam",
                table: "login_attempts",
                columns: new[] { "user_id", "ip", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_login_counters_blocked_until",
                schema: "iam",
                table: "login_counters",
                column: "blocked_until");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "login_attempts",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "login_counters",
                schema: "iam");
        }
    }
}
