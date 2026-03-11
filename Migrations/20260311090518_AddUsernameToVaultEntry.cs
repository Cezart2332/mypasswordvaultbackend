using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyPasswordVault.API.Migrations
{
    /// <inheritdoc />
    public partial class AddUsernameToVaultEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "VaultEntries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Username",
                table: "VaultEntries");
        }
    }
}
