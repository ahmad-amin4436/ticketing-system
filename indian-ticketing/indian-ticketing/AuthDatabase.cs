using Microsoft.Data.SqlClient;

namespace indian_ticketing;

// ═══════════════════════════════════════════════════════════════════════
//  SCHEMA BOOTSTRAP — creates the database/tables/seed data on first run,
//  idempotently (safe to call every launch). Four tables:
//    Roles            — named containers of permissions (Admin, Operator,
//                        or any custom role created later via the UI)
//    Permissions      — the fixed set of things the app can gate
//                        (MANAGE_CREDENTIALS, MANAGE_BOOKINGS, MANAGE_USERS)
//    RolePermissions  — which permissions each role grants (many-to-many)
//    Users            — accounts, each pointing at one role
//  This is what makes it a GENERIC rights system rather than a hardcoded
//  Admin/Operator switch: a new role with any subset of the fixed
//  permissions can be created from the Manage Roles screen without a code
//  change, and users can be assigned to it.
// ═══════════════════════════════════════════════════════════════════════
public static class AuthDatabase
{
    // The fixed set of things this app can gate. Adding a new gated
    // capability later means adding a row here (and a Session.Has(...)
    // check at the call site) — role/user management stays entirely
    // data-driven from that point on.
    public static readonly (string Code, string Description)[] AllPermissions =
    {
        ("MANAGE_CREDENTIALS", "Edit IRCTC login credentials and proxy settings"),
        ("MANAGE_BOOKINGS",    "Start, delete, and manage saved bookings"),
        ("MANAGE_USERS",       "Create/edit/delete users and manage roles"),
    };

    public static void EnsureReady(DbConfig cfg)
    {
        EnsureDatabaseExists(cfg);
        EnsureSchema(cfg);
        SeedDefaultRolesAndPermissions(cfg);
    }

    private static void EnsureDatabaseExists(DbConfig cfg)
    {
        using var conn = new SqlConnection(cfg.ConnectionString(includeDatabase: false));
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
IF DB_ID(@dbName) IS NULL
    EXEC('CREATE DATABASE [' + @dbName + ']');";
        cmd.Parameters.AddWithValue("@dbName", cfg.Database);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureSchema(DbConfig cfg)
    {
        using var conn = new SqlConnection(cfg.ConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
IF OBJECT_ID('dbo.Roles') IS NULL
BEGIN
    CREATE TABLE dbo.Roles (
        RoleId       INT IDENTITY(1,1) PRIMARY KEY,
        RoleName     NVARCHAR(50)  NOT NULL UNIQUE,
        IsSystemRole BIT           NOT NULL DEFAULT 0
    );
END

IF OBJECT_ID('dbo.Permissions') IS NULL
BEGIN
    CREATE TABLE dbo.Permissions (
        PermissionId   INT IDENTITY(1,1) PRIMARY KEY,
        PermissionCode NVARCHAR(50)  NOT NULL UNIQUE,
        Description    NVARCHAR(200) NULL
    );
END

IF OBJECT_ID('dbo.RolePermissions') IS NULL
BEGIN
    CREATE TABLE dbo.RolePermissions (
        RoleId       INT NOT NULL FOREIGN KEY REFERENCES dbo.Roles(RoleId) ON DELETE CASCADE,
        PermissionId INT NOT NULL FOREIGN KEY REFERENCES dbo.Permissions(PermissionId) ON DELETE CASCADE,
        PRIMARY KEY (RoleId, PermissionId)
    );
END

IF OBJECT_ID('dbo.Users') IS NULL
BEGIN
    CREATE TABLE dbo.Users (
        UserId       INT IDENTITY(1,1) PRIMARY KEY,
        Username     NVARCHAR(50)   NOT NULL UNIQUE,
        PasswordHash NVARCHAR(200)  NOT NULL,
        PasswordSalt NVARCHAR(200)  NOT NULL,
        RoleId       INT            NOT NULL FOREIGN KEY REFERENCES dbo.Roles(RoleId),
        IsActive     BIT            NOT NULL DEFAULT 1,
        CreatedAt    DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
    );
END";
        cmd.ExecuteNonQuery();
    }

    // Seeds the fixed permission list (idempotent — inserts only what's
    // missing) and, only if the Roles table is completely empty (true first
    // run), two starting roles: Admin (every permission) and Operator
    // (everything except MANAGE_USERS — can use credentials/proxy and run
    // bookings, just can't touch user/role management).
    private static void SeedDefaultRolesAndPermissions(DbConfig cfg)
    {
        using var conn = new SqlConnection(cfg.ConnectionString());
        conn.Open();

        foreach (var (code, desc) in AllPermissions)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE PermissionCode = @code)
    INSERT INTO dbo.Permissions (PermissionCode, Description) VALUES (@code, @desc);";
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@desc", desc);
            cmd.ExecuteNonQuery();
        }

        using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM dbo.Roles";
            var roleCount = (int)countCmd.ExecuteScalar();
            if (roleCount > 0) return; // already seeded (or roles were customized) — leave alone
        }

        int adminRoleId = InsertRole(conn, "Admin", isSystemRole: true);
        int operatorRoleId = InsertRole(conn, "Operator", isSystemRole: true);

        foreach (var (code, _) in AllPermissions)
            GrantPermission(conn, adminRoleId, code);

        // Operator: everything except user/role management.
        GrantPermission(conn, operatorRoleId, "MANAGE_CREDENTIALS");
        GrantPermission(conn, operatorRoleId, "MANAGE_BOOKINGS");
    }

    private static int InsertRole(SqlConnection conn, string name, bool isSystemRole)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO dbo.Roles (RoleName, IsSystemRole) OUTPUT INSERTED.RoleId VALUES (@name, @sys);";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@sys", isSystemRole);
        return (int)cmd.ExecuteScalar();
    }

    private static void GrantPermission(SqlConnection conn, int roleId, string permissionCode)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT @roleId, PermissionId FROM dbo.Permissions WHERE PermissionCode = @code;";
        cmd.Parameters.AddWithValue("@roleId", roleId);
        cmd.Parameters.AddWithValue("@code", permissionCode);
        cmd.ExecuteNonQuery();
    }
}
