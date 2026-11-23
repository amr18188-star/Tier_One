using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountPersonalData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "national_id",
                schema: "iam",
                table: "users",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                schema: "iam",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_national_id",
                schema: "iam",
                table: "users",
                column: "national_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_national_id",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "national_id",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "phone",
                schema: "iam",
                table: "users");
        }
    }
}
