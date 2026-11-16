using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CorePlasma.DAL.Migrations
{
    /// <inheritdoc />
    public partial class TankSessionsTankIdIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TankSessions_TankId",
                table: "TankSessions");

            migrationBuilder.CreateIndex(
                name: "IX_TankSessions_TankId_Id",
                table: "TankSessions",
                columns: new[] { "TankId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TankSessions_TankId_Id",
                table: "TankSessions");

            migrationBuilder.CreateIndex(
                name: "IX_TankSessions_TankId",
                table: "TankSessions",
                column: "TankId");
        }
    }
}
