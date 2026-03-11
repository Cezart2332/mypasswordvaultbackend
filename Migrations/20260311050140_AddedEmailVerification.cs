using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyPasswordVault.API.Migrations
{
    /// <inheritdoc />
    public partial class AddedEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "VaultEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "isVerified",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "VaultEntries");

            migrationBuilder.DropColumn(
                name: "isVerified",
                table: "Users");
        }
    }
}
