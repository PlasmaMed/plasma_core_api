using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CorePlasma.DAL.Migrations
{
    /// <summary>
    /// NEWAPP labels the two readings the wrong way round: "HighPressureAfter"/"LoadCellAfter" is the reading taken before use
    /// (pressure falls from one session to the next, but rises from Before to After inside a session).
    /// Swapping the column names fixes every imported row without rewriting the table; no model change.
    /// </summary>
    public partial class SwapTankSessionBeforeAfter : Migration
    {
        private static void Swap(MigrationBuilder migrationBuilder, string a, string b)
        {
            migrationBuilder.Sql($"""ALTER TABLE "TankSessions" RENAME COLUMN "{a}" TO "{a}_swap";""");
            migrationBuilder.Sql($"""ALTER TABLE "TankSessions" RENAME COLUMN "{b}" TO "{a}";""");
            migrationBuilder.Sql($"""ALTER TABLE "TankSessions" RENAME COLUMN "{a}_swap" TO "{b}";""");
        }

        private static void Log(MigrationBuilder migrationBuilder, string direction) =>
            migrationBuilder.Sql($"""
                INSERT INTO "DataChangeLogs" ("Id", "EntityName", "EntityId", "Action", "Changes", "ChangedByUsername", "ChangedByUserType", "ChangedDate")
                SELECT gen_random_uuid(), 'TankSessions', 'ALL', 'Updated',
                       jsonb_build_object('PressureBefore', jsonb_build_object('old', 'PressureAfter', 'new', 'PressureBefore'),
                                          'WeightBefore', jsonb_build_object('old', 'WeightAfter', 'new', 'WeightBefore'),
                                          'rows', (SELECT count(*) FROM "TankSessions"), 'migration', 'SwapTankSessionBeforeAfter ({direction})'),
                       'migration:SwapTankSessionBeforeAfter', 'System', now() AT TIME ZONE 'utc';
                """);

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Swap(migrationBuilder, "PressureBefore", "PressureAfter");
            Swap(migrationBuilder, "WeightBefore", "WeightAfter");
            Log(migrationBuilder, "up");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Swap(migrationBuilder, "PressureBefore", "PressureAfter");
            Swap(migrationBuilder, "WeightBefore", "WeightAfter");
            Log(migrationBuilder, "down");
        }
    }
}
