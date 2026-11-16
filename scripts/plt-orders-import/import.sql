-- Upsert PLT orders (plasma_med_center.Invoice) into "Orders" / "OrderTanks". Repeatable: matched on LegacyId.
-- Run from the folder holding orders.tsv / order_tanks.tsv (made by convert.py) with -v actor=<employee id>.
\set ON_ERROR_STOP on
SET client_encoding = 'UTF8';
BEGIN;
CREATE TEMP TABLE src_orders (legacy_id text, invoice_idx integer, type text, status text, delivery_option text, store text,
  client_user text, owner_user text, delivery_user text, note text, created_at timestamp, updated_at timestamp) ON COMMIT DROP;
CREATE TEMP TABLE src_order_tanks (order_legacy text, tank_legacy text, sort_order integer) ON COMMIT DROP;
\copy src_orders FROM 'orders.tsv'
\copy src_order_tanks FROM 'order_tanks.tsv'

-- Unknown PLT codes stop the import instead of being stored as something else.
DO $$ DECLARE bad text; BEGIN
  SELECT string_agg(DISTINCT v, ', ') INTO bad FROM (
    SELECT type v FROM src_orders WHERE type NOT IN ('NORMAL','DEMO')
    UNION ALL SELECT status FROM src_orders WHERE status NOT IN ('REQUEST','APPROVED','DELIVERING','DELIVERED','CANCELLED')
    UNION ALL SELECT delivery_option FROM src_orders WHERE delivery_option IS NOT NULL AND delivery_option NOT IN ('DIRECT','PICKUP','EXPRESS')) x;
  IF bad IS NOT NULL THEN RAISE EXCEPTION 'Unknown PLT order type/status/delivery option: %', bad; END IF;
END $$;

CREATE TEMP TABLE mapped ON COMMIT DROP AS
SELECT s.*,
       cu.customer_id, cu.customer_user_id,
       ow.employee_id AS created_by_employee_id,
       dl.employee_id AS delivery_employee_id,
       initcap(s.type) AS order_type_name,
       initcap(s.status) AS status_name,
       initcap(s.delivery_option) AS delivery_option_name
FROM src_orders s
LEFT JOIN plt_migration.source_users cu ON cu.source_id = s.client_user
LEFT JOIN plt_migration.source_users ow ON ow.source_id = s.owner_user
LEFT JOIN plt_migration.source_users dl ON dl.source_id = s.delivery_user;

CREATE TEMP TABLE upserted (id uuid, legacy_id text, inserted boolean) ON COMMIT DROP;
WITH up AS (
  INSERT INTO "Orders" ("Id", "OrderNumber", "OrderDate", "OrderType", "Status", "DeliveryOption", "Store", "CustomerId", "CustomerUserId",
                        "CreatedByEmployeeId", "DeliveryEmployeeId", "Note", "SourceUpdatedAt", "LegacyId", "CreatedDate", "CreatedBy", "IsDeleted")
  SELECT gen_random_uuid(), invoice_idx, created_at, order_type_name, status_name, delivery_option_name, store, customer_id, customer_user_id,
         created_by_employee_id, delivery_employee_id, nullif(note, ''), updated_at, legacy_id, now() AT TIME ZONE 'utc', :'actor', false
  FROM mapped
  ON CONFLICT ("LegacyId") WHERE "LegacyId" IS NOT NULL DO UPDATE SET
    "OrderNumber" = EXCLUDED."OrderNumber", "OrderDate" = EXCLUDED."OrderDate", "OrderType" = EXCLUDED."OrderType",
    "Status" = EXCLUDED."Status", "DeliveryOption" = EXCLUDED."DeliveryOption", "Store" = EXCLUDED."Store",
    "CustomerId" = EXCLUDED."CustomerId", "CustomerUserId" = EXCLUDED."CustomerUserId",
    "CreatedByEmployeeId" = EXCLUDED."CreatedByEmployeeId", "DeliveryEmployeeId" = EXCLUDED."DeliveryEmployeeId",
    "Note" = EXCLUDED."Note", "SourceUpdatedAt" = EXCLUDED."SourceUpdatedAt",
    "UpdatedDate" = now() AT TIME ZONE 'utc', "UpdatedBy" = EXCLUDED."CreatedBy"
  WHERE ("Orders"."SourceUpdatedAt" IS DISTINCT FROM EXCLUDED."SourceUpdatedAt") OR "Orders"."IsDeleted"
  RETURNING "Id", "LegacyId", (xmax = 0) AS inserted)
INSERT INTO upserted SELECT * FROM up;

-- Tanks of new or changed orders: replaced by the PLT list.
DELETE FROM "OrderTanks" ot USING upserted u WHERE ot."OrderId" = u.id;
INSERT INTO "OrderTanks" ("Id", "OrderId", "TankId", "TankLegacyId", "SortOrder", "CreatedDate", "CreatedBy", "IsDeleted")
SELECT gen_random_uuid(), u.id, t."Id", st.tank_legacy, st.sort_order, now() AT TIME ZONE 'utc', :'actor', false
FROM src_order_tanks st
JOIN upserted u ON u.legacy_id = st.order_legacy
LEFT JOIN "Tanks" t ON t."LegacyId" = st.tank_legacy;

-- One summary row per run (per-row logging of ~300 000 rows is not useful).
INSERT INTO "DataChangeLogs" ("Id", "EntityName", "EntityId", "Action", "Changes", "ChangedBy", "ChangedByUsername", "ChangedByUserType", "ChangedDate")
SELECT gen_random_uuid(), 'Orders', 'PLT-ORDERS', 'Imported',
       jsonb_build_object('source', 'plt1.plasmamed.com.vn / plasma_med_center.Invoice',
                          'sourceOrders', (SELECT count(*) FROM src_orders),
                          'inserted', (SELECT count(*) FROM upserted WHERE inserted),
                          'updated', (SELECT count(*) FROM upserted WHERE NOT inserted),
                          'orderTanks', (SELECT count(*) FROM "OrderTanks" ot JOIN upserted u ON u.id = ot."OrderId")),
       :'actor', 'import:PLT-orders', 'Employee', now() AT TIME ZONE 'utc';

SELECT (SELECT count(*) FROM src_orders) AS source_orders,
       (SELECT count(*) FROM upserted WHERE inserted) AS inserted,
       (SELECT count(*) FROM upserted WHERE NOT inserted) AS updated,
       (SELECT count(*) FROM mapped WHERE customer_id IS NULL) AS without_customer,
       (SELECT count(*) FROM mapped WHERE created_by_employee_id IS NULL) AS without_creator,
       (SELECT count(*) FROM mapped WHERE delivery_user IS NOT NULL AND delivery_employee_id IS NULL) AS delivery_user_unmapped,
       (SELECT count(*) FROM src_order_tanks st LEFT JOIN "Tanks" t ON t."LegacyId" = st.tank_legacy WHERE t."Id" IS NULL) AS tank_rows_unmatched;
COMMIT;
ANALYZE "Orders";
ANALYZE "OrderTanks";
