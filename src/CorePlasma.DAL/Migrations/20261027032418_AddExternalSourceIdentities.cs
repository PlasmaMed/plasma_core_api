using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CorePlasma.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalSourceIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LegacySyncMappings_Entity_CoreId",
                table: "LegacySyncMappings");

            migrationBuilder.DropIndex(
                name: "IX_LegacySyncMappings_Source_Entity_CoreId",
                table: "LegacySyncMappings");

            migrationBuilder.AddColumn<long>(
                name: "NewAppId",
                table: "Tanks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QuanLyId",
                table: "Tanks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSystem",
                table: "LegacySyncReceipts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceSystem",
                table: "LegacySyncMappings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "NewAppId",
                table: "Engines",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QuanLyId",
                table: "Engines",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "NewAppId",
                table: "Employees",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QuanLyId",
                table: "Employees",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "NewAppId",
                table: "Customers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QuanLyId",
                table: "Customers",
                type: "bigint",
                nullable: true);

            // Only previously verified migration ledgers may establish ownership. No name/phone/ID suffix guessing.
            migrationBuilder.Sql("""
                UPDATE public."LegacySyncMappings" SET "SourceSystem" = CASE "Source"
                    WHEN 'plasma-bv' THEN 'quanly.plasma.vn' WHEN 'newapp' THEN 'newapp.plasmamed.vn' ELSE '' END;
                UPDATE public."LegacySyncReceipts" SET "SourceSystem" = CASE "Source"
                    WHEN 'plasma-bv' THEN 'quanly.plasma.vn' WHEN 'newapp' THEN 'newapp.plasmamed.vn' ELSE '' END;
                DO $bootstrap$
                DECLARE native_table text; entity_name text; has_invalid_mapping boolean;
                BEGIN
                    IF EXISTS (SELECT 1 FROM public."LegacySyncMappings" WHERE "SourceSystem"='') OR
                       EXISTS (SELECT 1 FROM public."LegacySyncReceipts" WHERE "SourceSystem"='') THEN
                        RAISE EXCEPTION 'Unknown historical sync namespace: assign a reviewed source system before migrating.';
                    END IF;
                    IF to_regclass('newapp_migration.customer_map') IS NOT NULL THEN
                        INSERT INTO public."LegacySyncMappings"
                            ("Source","SourceSystem","Entity","EntityId","CoreId","LastSequence","PreserveCanonical","IsDeleted","SnapshotJson","UpdatedAt")
                        SELECT 'newapp','newapp.plasmamed.vn','customer',(r->>'source_id')::bigint,
                            (r->>'target_id')::uuid,0,false,
                            coalesce(d->>'IsDeleted',d->>'is_deleted','false')::boolean,
                            jsonb_build_object('id',(r->>'source_id')::bigint,
                                'name',coalesce(d->>'Name',d->>'name'),
                                'address',coalesce(d->>'Address',d->>'address'),
                                'phone_number',coalesce(d->>'Tel',d->>'tel',d->>'phone_number'),
                                'status',CASE WHEN coalesce(d->>'IsDeleted',d->>'is_deleted','false')::boolean THEN 'inactive' ELSE 'active' END,
                                'deleted_at',coalesce(d->'DeletedDateTime',d->'deleted_at')),now() AT TIME ZONE 'utc'
                        FROM (SELECT to_jsonb(m) r,coalesce(to_jsonb(m)->'source_data','{}'::jsonb) d
                            FROM newapp_migration.customer_map m) verified
                        ON CONFLICT ("Source","Entity","EntityId") DO NOTHING;
                        IF EXISTS (SELECT 1 FROM newapp_migration.customer_map m JOIN public."LegacySyncMappings" l
                            ON l."Source"='newapp' AND l."Entity"='customer' AND l."EntityId"=(to_jsonb(m)->>'source_id')::bigint
                            WHERE l."CoreId" IS DISTINCT FROM (to_jsonb(m)->>'target_id')::uuid) THEN
                            RAISE EXCEPTION 'NEWAPP customer ledger conflicts with an existing verified source mapping.';
                        END IF;
                    END IF;
                    IF to_regclass('newapp_migration.user_map') IS NOT NULL THEN
                        -- target_id is the login-account GUID; employee_id is the employee-profile GUID.
                        -- Copy only public profile data, never the raw source user row containing credentials.
                        INSERT INTO public."LegacySyncMappings"
                            ("Source","SourceSystem","Entity","EntityId","CoreId","LastSequence","PreserveCanonical","IsDeleted","SnapshotJson","UpdatedAt")
                        SELECT 'newapp','newapp.plasmamed.vn','user',(r->>'source_id')::bigint,
                            (r->>'employee_id')::uuid,0,false,
                            coalesce(d->>'IsDeleted',d->>'is_deleted','false')::boolean,
                            jsonb_build_object('id',(r->>'source_id')::bigint,
                                'name',coalesce(d->>'Name',d->>'name'),
                                'email',coalesce(d->>'Email',d->>'email'),
                                'phone_number',coalesce(d->>'PhoneNumber',d->>'phone_number'),
                                'is_active',coalesce(d->>'IsActive',d->>'is_active','false')::boolean,
                                'user_type',coalesce(d->'UserType',d->'user_type'),
                                'deleted_at',coalesce(d->'DeletedDateTime',d->'deleted_at')),now() AT TIME ZONE 'utc'
                        FROM (SELECT to_jsonb(m) r,coalesce(to_jsonb(m)->'source_data','{}'::jsonb) d
                            FROM newapp_migration.user_map m) verified
                        WHERE r->>'kind'='employee' AND nullif(r->>'employee_id','') IS NOT NULL
                            AND r->>'state' IN ('mapped_existing','planned_new')
                        ON CONFLICT ("Source","Entity","EntityId") DO NOTHING;
                        IF EXISTS (SELECT 1 FROM newapp_migration.user_map m JOIN public."LegacySyncMappings" l
                            ON l."Source"='newapp' AND l."Entity"='user' AND l."EntityId"=(to_jsonb(m)->>'source_id')::bigint
                            WHERE to_jsonb(m)->>'kind'='employee' AND nullif(to_jsonb(m)->>'employee_id','') IS NOT NULL
                            AND to_jsonb(m)->>'state' IN ('mapped_existing','planned_new')
                            AND l."CoreId" IS DISTINCT FROM (to_jsonb(m)->>'employee_id')::uuid) THEN
                            RAISE EXCEPTION 'NEWAPP employee ledger conflicts with an existing verified source mapping.';
                        END IF;
                    END IF;
                    UPDATE public."LegacySyncMappings" m SET "PreserveCanonical"=true
                    WHERE m."Entity"='customer' AND m."CoreId" IS NOT NULL AND
                        (SELECT count(*) FROM public."LegacySyncMappings" a
                            WHERE a."Entity"='customer' AND a."CoreId"=m."CoreId") > 1;
                    IF EXISTS (SELECT 1 FROM public."LegacySyncMappings" WHERE "EntityId"<=0 OR
                        "Entity" NOT IN ('customer','tank','engine','user') OR jsonb_typeof("SnapshotJson")<>'object') THEN
                        RAISE EXCEPTION 'Invalid authoritative source identity.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM public."LegacySyncMappings" WHERE "CoreId" IS NOT NULL
                        GROUP BY "SourceSystem","Entity","EntityId" HAVING count(DISTINCT "CoreId")>1) THEN
                        RAISE EXCEPTION 'One source-system ID points to multiple native Core records.';
                    END IF;
                    FOR native_table,entity_name IN SELECT * FROM (VALUES
                        ('Customers','customer'),('Tanks','tank'),('Engines','engine'),('Employees','user')) targets LOOP
                        EXECUTE format('SELECT EXISTS (SELECT 1 FROM public."LegacySyncMappings" m WHERE m."Entity"=$1 AND m."CoreId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM public.%I n WHERE n."Id"=m."CoreId"))',native_table)
                            INTO STRICT has_invalid_mapping USING entity_name;
                        IF has_invalid_mapping THEN RAISE EXCEPTION 'Verified % mapping points to a missing Core record.',entity_name; END IF;
                        EXECUTE format('UPDATE public.%I n SET "QuanLyId"=s.id FROM (SELECT "CoreId",min("EntityId") id FROM public."LegacySyncMappings" WHERE "Entity"=$1 AND "SourceSystem"=''quanly.plasma.vn'' AND "CoreId" IS NOT NULL GROUP BY "CoreId") s WHERE n."Id"=s."CoreId"',native_table) USING entity_name;
                        EXECUTE format('UPDATE public.%I n SET "NewAppId"=s.id FROM (SELECT "CoreId",min("EntityId") id FROM public."LegacySyncMappings" WHERE "Entity"=$1 AND "SourceSystem"=''newapp.plasmamed.vn'' AND "CoreId" IS NOT NULL GROUP BY "CoreId") s WHERE n."Id"=s."CoreId"',native_table) USING entity_name;
                    END LOOP;
                END $bootstrap$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Tanks_NewAppId",
                table: "Tanks",
                column: "NewAppId",
                unique: true,
                filter: "\"NewAppId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tanks_QuanLyId",
                table: "Tanks",
                column: "QuanLyId",
                unique: true,
                filter: "\"QuanLyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LegacySyncMappings_Entity_CoreId",
                table: "LegacySyncMappings",
                columns: new[] { "Entity", "CoreId" });

            migrationBuilder.CreateIndex(
                name: "IX_LegacySyncMappings_Source_Entity_CoreId",
                table: "LegacySyncMappings",
                columns: new[] { "Source", "Entity", "CoreId" },
                unique: true,
                filter: "\"Entity\" IN ('tank','engine','user') AND \"CoreId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Engines_NewAppId",
                table: "Engines",
                column: "NewAppId",
                unique: true,
                filter: "\"NewAppId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Engines_QuanLyId",
                table: "Engines",
                column: "QuanLyId",
                unique: true,
                filter: "\"QuanLyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_NewAppId",
                table: "Employees",
                column: "NewAppId",
                unique: true,
                filter: "\"NewAppId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_QuanLyId",
                table: "Employees",
                column: "QuanLyId",
                unique: true,
                filter: "\"QuanLyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_NewAppId",
                table: "Customers",
                column: "NewAppId",
                unique: true,
                filter: "\"NewAppId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_QuanLyId",
                table: "Customers",
                column: "QuanLyId",
                unique: true,
                filter: "\"QuanLyId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tanks_NewAppId",
                table: "Tanks");

            migrationBuilder.DropIndex(
                name: "IX_Tanks_QuanLyId",
                table: "Tanks");

            migrationBuilder.DropIndex(
                name: "IX_LegacySyncMappings_Entity_CoreId",
                table: "LegacySyncMappings");

            migrationBuilder.DropIndex(
                name: "IX_LegacySyncMappings_Source_Entity_CoreId",
                table: "LegacySyncMappings");

            migrationBuilder.DropIndex(
                name: "IX_Engines_NewAppId",
                table: "Engines");

            migrationBuilder.DropIndex(
                name: "IX_Engines_QuanLyId",
                table: "Engines");

            migrationBuilder.DropIndex(
                name: "IX_Employees_NewAppId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_QuanLyId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Customers_NewAppId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_QuanLyId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "NewAppId",
                table: "Tanks");

            migrationBuilder.DropColumn(
                name: "QuanLyId",
                table: "Tanks");

            migrationBuilder.DropColumn(
                name: "SourceSystem",
                table: "LegacySyncReceipts");

            migrationBuilder.DropColumn(
                name: "SourceSystem",
                table: "LegacySyncMappings");

            migrationBuilder.DropColumn(
                name: "NewAppId",
                table: "Engines");

            migrationBuilder.DropColumn(
                name: "QuanLyId",
                table: "Engines");

            migrationBuilder.DropColumn(
                name: "NewAppId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "QuanLyId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "NewAppId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "QuanLyId",
                table: "Customers");

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
        }
    }
}
