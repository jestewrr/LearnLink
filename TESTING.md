# LearnLink Testing Report

## 1) Tests Conducted

### A. Build Validation (Executed)
- **Scope:** Entire solution compile check after backup dashboard wiring updates.
- **Command used:** `dotnet build`
- **Result:** Passed (build succeeded; warnings present but no blocking errors).

### B. EF Migration Validation (Executed)
- **Scope:** Migration application for backup-related schema changes.
- **Commands used:**
  - `dotnet ef database update --verbose` (remote DB attempt)
  - `dotnet ef database update --connection "Server=(localdb)\MSSQLLocalDB;..."` (LocalDB fallback)
- **Result:**
  - Remote SQL endpoint was unreachable from current environment.
  - LocalDB migration execution succeeded and applied pending migrations.

### C. Backup Dashboard Wiring Verification (Code-Level + Build)
- **Scope:** Ensured `BackupDashboard` now loads metrics via `IBackupService.CalculateStorageMetricsAsync()` and binds repository selections in `TriggerManualBackup`.
- **Method:** Code verification + successful compile.
- **Result:** Controller wiring is in place and compiles.

## 2) Tools Used

| Tool | Used? | Purpose |
|---|---|---|
| Dotnet CLI (`dotnet build`, `dotnet ef`) | Yes | Build + migration validation |
| PowerShell Terminal | Yes | Command execution and repo operations |
| Git | Yes | Version control and push |
| Postman | No | Not used in this testing pass |
| Jest | No | Not applicable (no JS unit test suite run in this pass) |
| PyTest | No | Not applicable (project is not Python-based for this scope) |

## 3) Documented Test Cases

### TC-01: Backup dashboard loads non-zero metrics when data exists
- **Preconditions:** Database has users/resources/audit logs.
- **Steps:**
  1. Login as `SuperAdmin` or `Manager`.
  2. Open `Backup Dashboard`.
- **Expected Result:** Metric cards show actual counts (not all zeros).
- **Status:** Ready for manual verification in UI.

### TC-02: Backup dashboard handles empty dataset
- **Preconditions:** Fresh/empty database.
- **Steps:**
  1. Login with authorized role.
  2. Open `Backup Dashboard`.
- **Expected Result:** Counts show `0` without runtime errors.
- **Status:** Ready for manual verification.

### TC-03: Manual backup with selected repositories
- **Preconditions:** Authorized user on backup dashboard.
- **Steps:**
  1. Select one or more repository checkboxes.
  2. Click `Create Backup`.
- **Expected Result:** `TriggerManualBackup` receives selected items, backup starts, success message appears.
- **Status:** Ready for manual verification.

### TC-04: Manual backup with no repositories selected
- **Preconditions:** Authorized user on backup dashboard.
- **Steps:**
  1. Leave all repository checkboxes unselected.
  2. Click `Create Backup`.
- **Expected Result:** Fallback behavior triggers full/default backup scope without exception.
- **Status:** Ready for manual verification.

### TC-05: Unauthorized access to backup routes
- **Preconditions:** User not logged in or lacks required role.
- **Steps:**
  1. Directly navigate to backup dashboard URL.
  2. Attempt to POST manual backup endpoint.
- **Expected Result:** Access denied or redirected to login; no backup triggered.
- **Status:** Ready for manual verification.

### TC-06: Backup record persistence after trigger
- **Preconditions:** Manual backup was triggered.
- **Steps:**
  1. Query backup tables or inspect restore history section.
  2. Confirm new backup entry.
- **Expected Result:** New backup metadata appears with timestamp and status.
- **Status:** Ready for validation.

### TC-07: LocalDB migration applies enterprise backup schema
- **Preconditions:** LocalDB available.
- **Steps:**
  1. Run `dotnet ef database update` against LocalDB.
  2. Inspect migration history table.
- **Expected Result:** Latest migration is applied successfully.
- **Status:** Executed (passed on LocalDB).

### TC-08: Remote DB migration connectivity check
- **Preconditions:** Network route to remote SQL host.
- **Steps:**
  1. Run `dotnet ef database update --verbose` for remote connection.
- **Expected Result:** Migration applies if network and credentials are valid.
- **Status:** Executed in this session (blocked by network reachability).

## 4) Summary
- Core compile and LocalDB migration checks passed.
- Backup dashboard/controller wiring changes compile and are ready for functional UI verification.
- Postman, Jest, and PyTest were **not** used in this pass.