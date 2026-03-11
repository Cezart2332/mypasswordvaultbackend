using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyPasswordVault.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVaultEntryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Username",
                table: "VaultEntries",
                newName: "Url");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "VaultEntries",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "VaultEntries");

            migrationBuilder.RenameColumn(
                name: "Url",
                table: "VaultEntries",
                newName: "Username");
        }
    }
}
