using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CorePlasma.DAL.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyClientUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tanks_LegacyClientUserId",
                table: "Tanks");

            migrationBuilder.DropIndex(
                name: "IX_Engines_LegacyClientUserId",
                table: "Engines");

            migrationBuilder.DropColumn(
                name: "LegacyClientUserId",
                table: "Tanks");

            migrationBuilder.DropColumn(
                name: "LegacyClientUserId",
                table: "Engines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LegacyClientUserId",
                table: "Tanks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyClientUserId",
                table: "Engines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tanks_LegacyClientUserId",
                table: "Tanks",
                column: "LegacyClientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Engines_LegacyClientUserId",
                table: "Engines",
                column: "LegacyClientUserId");
        }
    }
}
