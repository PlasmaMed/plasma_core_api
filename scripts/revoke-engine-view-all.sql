-- One-off, idempotent: only Super Admin sees every machine; every other user only sees the machines he manages
-- (Engine.ResponsibleEmployeeId). Revokes the ENGINE_VIEW_ALL grants made automatically on 07/10/2026 when the
-- permission was introduced. Grants given by hand in "Phân quyền theo chức năng" are left untouched.
\set ON_ERROR_STOP on
BEGIN;
CREATE TEMP TABLE revokes ON COMMIT DROP AS
SELECT ep."EmployeeId" AS employee_id, ep."PermissionId" AS permission_id,
       json_build_object('IsActive', ep."IsActive", 'GrantedDate', ep."GrantedDate")::text AS old_value
FROM "EmployeePermissions" ep
JOIN "Permissions" p ON p."Id" = ep."PermissionId" AND p."PermissionCode" = 'ENGINE_VIEW_ALL'
WHERE EXISTS (SELECT 1 FROM "PermissionAuditLogs" a
              WHERE a."EmployeeId" = ep."EmployeeId" AND a."PermissionId" = ep."PermissionId"
                AND a."NewValue" LIKE '%ENGINE_VIEW_ALL introduced%');
DELETE FROM "EmployeePermissions" ep USING revokes r WHERE ep."EmployeeId" = r.employee_id AND ep."PermissionId" = r.permission_id;
INSERT INTO "PermissionAuditLogs" ("Id", "EmployeeId", "PermissionId", "Action", "ChangedBy", "ChangedDate", "OldValue", "NewValue")
SELECT gen_random_uuid(), employee_id, permission_id, 'REVOKE', (SELECT "Id" FROM "Employees" WHERE "EmployeeCode" = 'ADMIN'),
       now() AT TIME ZONE 'utc', old_value, '{"Reason": "Only Super Admin sees every machine; users see the machines they manage"}'
FROM revokes;
SELECT count(*) AS revoked FROM revokes;
SELECT e."EmployeeCode", e."FullName", e."Department" AS still_has_view_all
FROM "EmployeePermissions" ep JOIN "Permissions" p ON p."Id" = ep."PermissionId" JOIN "Employees" e ON e."Id" = ep."EmployeeId"
WHERE p."PermissionCode" = 'ENGINE_VIEW_ALL';
COMMIT;
