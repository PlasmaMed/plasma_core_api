-- One-off, idempotent: give the "Đơn hàng" menu and ORDER_VIEW to every active employee who already sees the tank menu.
-- Without ORDER_VIEW_ALL (Super Admin only by default) an employee only sees the orders he created or delivers.
-- Run after the API has started once (the seeder creates the ORDER_* / MENU_ORDERS permissions).
\set ON_ERROR_STOP on
BEGIN;
DO $$ BEGIN
  IF (SELECT count(*) FROM "Permissions" WHERE "PermissionCode" IN ('ORDER_VIEW', 'MENU_ORDERS')) <> 2 THEN
    RAISE EXCEPTION 'ORDER_VIEW / MENU_ORDERS do not exist yet: start the API once so the seeder creates them.';
  END IF;
END $$;
CREATE TEMP TABLE grants ON COMMIT DROP AS
SELECT DISTINCT ep."EmployeeId" AS employee_id, p."Id" AS permission_id, p."PermissionCode" AS code
FROM "EmployeePermissions" ep
JOIN "Permissions" mt ON mt."Id" = ep."PermissionId" AND mt."PermissionCode" = 'MENU_TANKS'
JOIN "Employees" e ON e."Id" = ep."EmployeeId" AND NOT e."IsDeleted"
CROSS JOIN "Permissions" p
WHERE ep."IsActive" AND p."PermissionCode" IN ('ORDER_VIEW', 'MENU_ORDERS')
  AND NOT EXISTS (SELECT 1 FROM "EmployeePermissions" x WHERE x."EmployeeId" = ep."EmployeeId" AND x."PermissionId" = p."Id");
INSERT INTO "EmployeePermissions" ("EmployeeId", "PermissionId", "GrantedDate", "GrantedBy", "IsActive")
SELECT employee_id, permission_id, now() AT TIME ZONE 'utc', (SELECT "Id" FROM "Employees" WHERE "EmployeeCode" = 'ADMIN'), true FROM grants;
INSERT INTO "PermissionAuditLogs" ("Id", "EmployeeId", "PermissionId", "Action", "ChangedBy", "ChangedDate", "NewValue")
SELECT gen_random_uuid(), employee_id, permission_id, 'GRANT', (SELECT "Id" FROM "Employees" WHERE "EmployeeCode" = 'ADMIN'),
       now() AT TIME ZONE 'utc', '{"IsActive": true, "Reason": "Đơn hàng menu added (orders imported from PLT)"}'
FROM grants;
SELECT code, count(*) AS granted FROM grants GROUP BY 1 ORDER BY 1;
COMMIT;
