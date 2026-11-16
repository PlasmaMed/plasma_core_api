using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CorePlasma.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddDataChangeLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataChangeLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Changes = table.Column<string>(type: "jsonb", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedByUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ChangedByUserType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ChangedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataChangeLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataChangeLogs_ChangedBy",
                table: "DataChangeLogs",
                column: "ChangedBy");

            migrationBuilder.CreateIndex(
                name: "IX_DataChangeLogs_ChangedDate",
                table: "DataChangeLogs",
                column: "ChangedDate");

            migrationBuilder.CreateIndex(
                name: "IX_DataChangeLogs_EntityName_EntityId_ChangedDate",
                table: "DataChangeLogs",
                columns: new[] { "EntityName", "EntityId", "ChangedDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataChangeLogs");
        }
    }
}
