using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CorePlasma.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SourceRevision",
                table: "Sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "HistoryIngestReceipts",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceSessionId = table.Column<long>(type: "bigint", nullable: false),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Disposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TankSessionsUpserted = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoryIngestReceipts", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_HistoryIngestReceipts_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoryIngestReceipts_SessionId",
                table: "HistoryIngestReceipts",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoryIngestReceipts_Source_SourceRevision",
                table: "HistoryIngestReceipts",
                columns: new[] { "Source", "SourceRevision" },
                unique: true);

            // Match the receiver-aware latest-usage queries without sorting the entire history.
            // Positive NEWAPP sequence IDs precede null-source records (which use UUID insertion order).
            // Separate commands outside the migration transaction allow production history writes to continue.
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY "IX_TankSessions_TankId_SourceSequence_Id"
                    ON "TankSessions" ("TankId", (COALESCE("LegacyId", 0::bigint)) DESC, "Id" DESC);
                """, suppressTransaction: true);
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY "IX_Sessions_EngineId_SourceSequence_Id"
                    ON "Sessions" ("EngineId", (COALESCE("LegacyId", 0::bigint)) DESC, "Id" DESC);
                """, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_TankSessions_TankId_SourceSequence_Id", table: "TankSessions");
            migrationBuilder.DropIndex(name: "IX_Sessions_EngineId_SourceSequence_Id", table: "Sessions");
            migrationBuilder.DropTable(
                name: "HistoryIngestReceipts");

            migrationBuilder.DropColumn(
                name: "SourceRevision",
                table: "Sessions");
        }
    }
}
