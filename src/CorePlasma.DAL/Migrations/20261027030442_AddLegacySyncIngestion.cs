using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CorePlasma.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddLegacySyncIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegacySyncMappings",
                columns: table => new
                {
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Entity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EntityId = table.Column<long>(type: "bigint", nullable: false),
                    CoreId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    PreserveCanonical = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacySyncMappings", x => new { x.Source, x.Entity, x.EntityId });
                });

            migrationBuilder.CreateTable(
                name: "LegacySyncReceipts",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Entity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EntityId = table.Column<long>(type: "bigint", nullable: false),
                    CoreId = table.Column<Guid>(type: "uuid", nullable: true),
                    Operation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacySyncReceipts", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegacySyncMappings_Entity_CoreId",
                table: "LegacySyncMappings",
                columns: new[] { "Entity", "CoreId" },
                unique: true,
                filter: "\"Entity\" = 'tank' AND \"CoreId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LegacySyncMappings_Source_Entity_CoreId",
                table: "LegacySyncMappings",
                columns: new[] { "Source", "Entity", "CoreId" });

            migrationBuilder.CreateIndex(
                name: "IX_LegacySyncReceipts_Source_Sequence",
                table: "LegacySyncReceipts",
                columns: new[] { "Source", "Sequence" },
                unique: true);

            // Reuse verified production identities from the earlier plasma → Core import.
            // No native customer/tank is changed here; merged customer ownership stays many-to-one.
            migrationBuilder.Sql("""
                DO $bootstrap$
                BEGIN
                    IF to_regclass('plasma_migration.customer_map') IS NOT NULL THEN
                        INSERT INTO public."LegacySyncMappings"
                            ("Source","Entity","EntityId","CoreId","LastSequence","PreserveCanonical","IsDeleted","SnapshotJson","UpdatedAt")
                        SELECT 'plasma-bv','customer',source_id,target_id,0,
                            target_id IS NOT NULL AND count(*) OVER (PARTITION BY target_id) > 1,
                            coalesce(source_data->>'deleted_at','') <> '',source_data,synced_at
                        FROM plasma_migration.customer_map;
                    END IF;
                    IF to_regclass('plasma_migration.tank_map') IS NOT NULL THEN
                        INSERT INTO public."LegacySyncMappings"
                            ("Source","Entity","EntityId","CoreId","LastSequence","PreserveCanonical","IsDeleted","SnapshotJson","UpdatedAt")
                        SELECT 'plasma-bv','tank',source_id,target_id,0,false,
                            coalesce(source_data->>'deleted_at','') <> '',source_data,synced_at
                        FROM plasma_migration.tank_map;
                    END IF;
                    IF to_regclass('plasma_migration.customer_merges') IS NOT NULL THEN
                        UPDATE public."LegacySyncMappings" m SET "PreserveCanonical"=true
                        WHERE m."Source"='plasma-bv' AND m."Entity"='customer' AND m."CoreId" IN
                            (SELECT survivor_customer_id FROM plasma_migration.customer_merges);
                    END IF;
                    IF EXISTS (SELECT 1 FROM public."LegacySyncMappings" m
                        WHERE m."EntityId" <= 0 OR jsonb_typeof(m."SnapshotJson") <> 'object'
                        OR (m."Entity"='customer' AND m."CoreId" IS NOT NULL AND NOT EXISTS
                            (SELECT 1 FROM public."Customers" c WHERE c."Id"=m."CoreId"))
                        OR (m."Entity"='tank' AND (m."CoreId" IS NULL OR NOT EXISTS
                            (SELECT 1 FROM public."Tanks" t WHERE t."Id"=m."CoreId")))) THEN
                        RAISE EXCEPTION 'Legacy sync bootstrap mapping is invalid; verify the prior import lineage before enabling delivery.';
                    END IF;
                END $bootstrap$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegacySyncMappings");

            migrationBuilder.DropTable(
                name: "LegacySyncReceipts");
        }
    }
}
