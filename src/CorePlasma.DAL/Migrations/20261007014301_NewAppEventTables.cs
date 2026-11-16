using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CorePlasma.DAL.Migrations
{
    /// <inheritdoc />
    public partial class NewAppEventTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TankSessions_IsDeleted",
                table: "TankSessions");

            migrationBuilder.DropIndex(
                name: "IX_TankSessions_TankRfid",
                table: "TankSessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_IsDeleted",
                table: "Sessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sessions_EndTime",
                table: "Sessions");

            migrationBuilder.AlterColumn<decimal>(
                name: "WeightBefore",
                table: "TankSessions",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "WeightAfter",
                table: "TankSessions",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TankRfid",
                table: "TankSessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<decimal>(
                name: "PressureBefore",
                table: "TankSessions",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PressureAfter",
                table: "TankSessions",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LegacyId",
                table: "TankSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "TankSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TankId",
                table: "TankSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AverageSpeed",
                table: "Sessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnvironmentTemperatureAfter",
                table: "Sessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnvironmentTemperatureBefore",
                table: "Sessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "Sessions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandleTemperatureAfter",
                table: "Sessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandleTemperatureBefore",
                table: "Sessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LegacyId",
                table: "Sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "Sessions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlasmaOnTime",
                table: "Sessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserCode",
                table: "Sessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TankEngineHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EngineId = table.Column<Guid>(type: "uuid", nullable: false),
                    TankRfid = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TankId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    LegacyId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TankEngineHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TankEngineHistories_Engines_EngineId",
                        column: x => x.EngineId,
                        principalTable: "Engines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TankEngineHistories_Tanks_TankId",
                        column: x => x.TankId,
                        principalTable: "Tanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TankSessions_LegacyId",
                table: "TankSessions",
                column: "LegacyId",
                unique: true,
                filter: "\"LegacyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TankSessions_TankId",
                table: "TankSessions",
                column: "TankId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_LegacyId",
                table: "Sessions",
                column: "LegacyId",
                unique: true,
                filter: "\"LegacyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TankEngineHistories_EngineId_EventTime",
                table: "TankEngineHistories",
                columns: new[] { "EngineId", "EventTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TankEngineHistories_LegacyId",
                table: "TankEngineHistories",
                column: "LegacyId",
                unique: true,
                filter: "\"LegacyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TankEngineHistories_TankId",
                table: "TankEngineHistories",
                column: "TankId");

            migrationBuilder.AddForeignKey(
                name: "FK_TankSessions_Tanks_TankId",
                table: "TankSessions",
                column: "TankId",
                principalTable: "Tanks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Case-insensitive RFID lookup: WHERE upper("TankRfid") = @p
            migrationBuilder.Sql("CREATE INDEX \"IX_TankSessions_TankRfid_Upper\" ON \"TankSessions\" (upper(\"TankRfid\"));");
            migrationBuilder.Sql("CREATE INDEX \"IX_TankEngineHistories_TankRfid_Upper\" ON \"TankEngineHistories\" (upper(\"TankRfid\"));");
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_TankSessions_TankRfid_Upper\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_TankEngineHistories_TankRfid_Upper\";");

            migrationBuilder.DropForeignKey(
                name: "FK_TankSessions_Tanks_TankId",
                table: "TankSessions");

            migrationBuilder.DropTable(
                name: "TankEngineHistories");

            migrationBuilder.DropIndex(
                name: "IX_TankSessions_LegacyId",
                table: "TankSessions");

            migrationBuilder.DropIndex(
                name: "IX_TankSessions_TankId",
                table: "TankSessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_LegacyId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "LegacyId",
                table: "TankSessions");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "TankSessions");

            migrationBuilder.DropColumn(
                name: "TankId",
                table: "TankSessions");

            migrationBuilder.DropColumn(
                name: "AverageSpeed",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "EnvironmentTemperatureAfter",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "EnvironmentTemperatureBefore",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "HandleTemperatureAfter",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "HandleTemperatureBefore",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "LegacyId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PlasmaOnTime",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "UserCode",
                table: "Sessions");

            migrationBuilder.AlterColumn<decimal>(
                name: "WeightBefore",
                table: "TankSessions",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "WeightAfter",
                table: "TankSessions",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TankRfid",
                table: "TankSessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PressureBefore",
                table: "TankSessions",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PressureAfter",
                table: "TankSessions",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TankSessions_IsDeleted",
                table: "TankSessions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_TankSessions_TankRfid",
                table: "TankSessions",
                column: "TankRfid");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_IsDeleted",
                table: "Sessions",
                column: "IsDeleted");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sessions_EndTime",
                table: "Sessions",
                sql: "\"EndTime\" IS NULL OR \"EndTime\" >= \"StartTime\"");
        }
    }
}
