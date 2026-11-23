using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EncryptNationalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0021 DEC-D5: the clear-text column lived for one commit and is dropped, not converted: encryption needs the
            // application's keys, which a migration does not have. An account that had a number in it shows "incomplete data" again
            // and takes the number once more. No database held a real number when this was written.
            migrationBuilder.DropIndex(
                name: "ix_users_national_id",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "national_id",
                schema: "iam",
                table: "users");

            migrationBuilder.AddColumn<string>(
                name: "national_id_hash",
                schema: "iam",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "national_id_protected",
                schema: "iam",
                table: "users",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_national_id_hash",
                schema: "iam",
                table: "users",
                column: "national_id_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_national_id_hash",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "national_id_hash",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "national_id_protected",
                schema: "iam",
                table: "users");

            migrationBuilder.AddColumn<string>(
                name: "national_id",
                schema: "iam",
                table: "users",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_national_id",
                schema: "iam",
                table: "users",
                column: "national_id",
                unique: true);
        }
    }
}
