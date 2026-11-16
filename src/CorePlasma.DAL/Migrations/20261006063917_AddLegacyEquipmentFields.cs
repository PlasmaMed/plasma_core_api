using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CorePlasma.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddLegacyEquipmentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LegacyClientUserId",
                table: "Tanks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyId",
                table: "Tanks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyInvoiceId",
                table: "Tanks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Store",
                table: "Tanks",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TankIndex",
                table: "Tanks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EngineType",
                table: "Engines",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EquipmentIndex",
                table: "Engines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasMaintenance",
                table: "Engines",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasWarranty",
                table: "Engines",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyClientUserId",
                table: "Engines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyId",
                table: "Engines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Store",
                table: "Engines",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TankType",
                table: "Engines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WarrantyStartDate",
                table: "Engines",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tanks_LegacyClientUserId",
                table: "Tanks",
                column: "LegacyClientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tanks_LegacyId",
                table: "Tanks",
                column: "LegacyId",
                unique: true,
                filter: "\"LegacyId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Tanks_Store",
                table: "Tanks",
                column: "Store");

            migrationBuilder.CreateIndex(
                name: "IX_Tanks_TankType",
                table: "Tanks",
                column: "TankType");

            migrationBuilder.CreateIndex(
                name: "IX_Engines_EngineType",
                table: "Engines",
                column: "EngineType");

            migrationBuilder.CreateIndex(
                name: "IX_Engines_LegacyClientUserId",
                table: "Engines",
                column: "LegacyClientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Engines_LegacyId",
                table: "Engines",
                column: "LegacyId",
                unique: true,
                filter: "\"LegacyId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Engines_Store",
                table: "Engines",
                column: "Store");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tanks_LegacyClientUserId",
                table: "Tanks");

            migrationBuilder.DropIndex(
                name: "IX_Tanks_LegacyId",
                table: "Tanks");

            migrationBuilder.DropIndex(
                name: "IX_Tanks_Store",
                table: "Tanks");

            migrationBuilder.DropIndex(
                name: "IX_Tanks_TankType",
                table: "Tanks");

            migrationBuilder.DropIndex(
                name: "IX_Engines_EngineType",
                table: "Engines");

            migrationBuilder.DropIndex(
                name: "IX_Engines_LegacyClientUserId",
                table: "Engines");

            migrationBuilder.DropIndex(
                name: "IX_Engines_LegacyId",
                table: "Engines");

            migrationBuilder.DropIndex(
                name: "IX_Engines_Store",
                table: "Engines");

            migrationBuilder.DropColumn(
                name: "LegacyClientUserId",
                table: "Tanks");

            migrationBuilder.DropColumn(
                name: "LegacyId",
                table: "Tanks");

            migrationBuilder.DropColumn(
                name: "LegacyInvoiceId",
                table: "Tanks");

            migrationBuilder.DropColumn(
                name: "Store",
                table: "Tanks");

            migrationBuilder.DropColumn(
                name: "TankIndex",
                table: "Tanks");

            migrationBuilder.DropColumn(
                name: "EngineType",
                table: "Engines");

            migrationBuilder.DropColumn(
                name: "EquipmentIndex",
                table: "Engines");

            migrationBuilder.DropColumn(
                name: "HasMaintenance",
                table: "Engines");

            migrationBuilder.DropColumn(
                name: "HasWarranty",
                table: "Engines");

            migrationBuilder.DropColumn(
                name: "LegacyClientUserId",
                table: "Engines");

            migrationBuilder.DropColumn(
                name: "LegacyId",
                table: "Engines");

            migrationBuilder.DropColumn(
                name: "Store",
                table: "Engines");

            migrationBuilder.DropColumn(
                name: "TankType",
                table: "Engines");

            migrationBuilder.DropColumn(
                name: "WarrantyStartDate",
                table: "Engines");
        }
    }
}
