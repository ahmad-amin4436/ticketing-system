using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace indian_ticketing;

public class UserAccount
{
    public int    UserId   { get; set; }
    public string Username { get; set; } = "";
    public int    RoleId   { get; set; }
    public string RoleName { get; set; } = "";
    public bool   IsActive { get; set; } = true;
}

public class RoleInfo
{
    public int    RoleId       { get; set; }
    public string RoleName     { get; set; } = "";
    public bool   IsSystemRole { get; set; }
}

// PBKDF2 password hashing — same approach used throughout this app's other
// local credential handling, just now storing the salt/hash as separate
// NVARCHAR columns instead of a single JSON field.
internal static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int HashSizeBytes = 32;

    public static (string Hash, string Salt) Create(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    public static bool Verify(string password, string storedHash, string storedSalt)
    {
        try
        {
            var saltBytes = Convert.FromBase64String(storedSalt);
            var actual    = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
            var expected  = Convert.FromBase64String(storedHash);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  SQL-BACKED AUTH REPOSITORY — all reads/writes against the Users/Roles/
//  Permissions/RolePermissions tables. Synchronous (matching this codebase's
//  existing local-store pattern — ProxyConfig/SavedBooking are also plain
//  synchronous calls) — acceptable here since it's a local SQL Express
//  instance, not a remote server.
// ═══════════════════════════════════════════════════════════════════════
public static class AuthRepository
{
    private static SqlConnection Open()
    {
        var conn = new SqlConnection(DbConfig.Load().ConnectionString());
        conn.Open();
        return conn;
    }

    public static bool HasAnyUsers()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dbo.Users";
        return (int)cmd.ExecuteScalar() > 0;
    }

    // Returns the authenticated user (with role + permission set loaded) or
    // null if the username/password don't match an active account.
    public static UserAccount? Authenticate(string username, string password)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT u.UserId, u.Username, u.PasswordHash, u.PasswordSalt, u.IsActive, r.RoleId, r.RoleName
FROM dbo.Users u JOIN dbo.Roles r ON r.RoleId = u.RoleId
WHERE u.Username = @username;";
        cmd.Parameters.AddWithValue("@username", username);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var isActive = reader.GetBoolean(4);
        var hash = reader.GetString(2);
        var salt = reader.GetString(3);
        if (!isActive || !PasswordHasher.Verify(password, hash, salt)) return null;

        return new UserAccount
        {
            UserId   = reader.GetInt32(0),
            Username = reader.GetString(1),
            IsActive = isActive,
            RoleId   = reader.GetInt32(5),
            RoleName = reader.GetString(6),
        };
    }

    public static HashSet<string> GetPermissionsForRole(int roleId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT p.PermissionCode
FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.PermissionId = rp.PermissionId
WHERE rp.RoleId = @roleId;";
        cmd.Parameters.AddWithValue("@roleId", roleId);
        using var reader = cmd.ExecuteReader();
        var set = new HashSet<string>();
        while (reader.Read()) set.Add(reader.GetString(0));
        return set;
    }

    public static List<UserAccount> GetAllUsers()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT u.UserId, u.Username, u.IsActive, r.RoleId, r.RoleName
FROM dbo.Users u JOIN dbo.Roles r ON r.RoleId = u.RoleId
ORDER BY u.Username;";
        using var reader = cmd.ExecuteReader();
        var list = new List<UserAccount>();
        while (reader.Read())
        {
            list.Add(new UserAccount
            {
                UserId   = reader.GetInt32(0),
                Username = reader.GetString(1),
                IsActive = reader.GetBoolean(2),
                RoleId   = reader.GetInt32(3),
                RoleName = reader.GetString(4),
            });
        }
        return list;
    }

    public static List<RoleInfo> GetAllRoles()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT RoleId, RoleName, IsSystemRole FROM dbo.Roles ORDER BY RoleName;";
        using var reader = cmd.ExecuteReader();
        var list = new List<RoleInfo>();
        while (reader.Read())
        {
            list.Add(new RoleInfo
            {
                RoleId       = reader.GetInt32(0),
                RoleName     = reader.GetString(1),
                IsSystemRole = reader.GetBoolean(2),
            });
        }
        return list;
    }

    public static bool UsernameExists(string username)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dbo.Users WHERE Username = @u;";
        cmd.Parameters.AddWithValue("@u", username);
        return (int)cmd.ExecuteScalar() > 0;
    }

    public static void CreateUser(string username, string password, int roleId)
    {
        var (hash, salt) = PasswordHasher.Create(password);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO dbo.Users (Username, PasswordHash, PasswordSalt, RoleId, IsActive)
VALUES (@u, @h, @s, @r, 1);";
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@h", hash);
        cmd.Parameters.AddWithValue("@s", salt);
        cmd.Parameters.AddWithValue("@r", roleId);
        cmd.ExecuteNonQuery();
    }

    public static void ResetPassword(int userId, string newPassword)
    {
        var (hash, salt) = PasswordHasher.Create(newPassword);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Users SET PasswordHash = @h, PasswordSalt = @s WHERE UserId = @id;";
        cmd.Parameters.AddWithValue("@h", hash);
        cmd.Parameters.AddWithValue("@s", salt);
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
    }

    public static void ChangeUserRole(int userId, int newRoleId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Users SET RoleId = @r WHERE UserId = @id;";
        cmd.Parameters.AddWithValue("@r", newRoleId);
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteUser(int userId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM dbo.Users WHERE UserId = @id;";
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
    }

    public static int CountUsersInRole(int roleId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dbo.Users WHERE RoleId = @r;";
        cmd.Parameters.AddWithValue("@r", roleId);
        return (int)cmd.ExecuteScalar();
    }

    // ── Role/permission management (the "generic" part — new roles with
    //    any subset of the fixed permission set can be created here). ──────
    public static int CreateRole(string name)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO dbo.Roles (RoleName, IsSystemRole) OUTPUT INSERTED.RoleId VALUES (@n, 0);";
        cmd.Parameters.AddWithValue("@n", name);
        return (int)cmd.ExecuteScalar();
    }

    public static void DeleteRole(int roleId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM dbo.Roles WHERE RoleId = @id AND IsSystemRole = 0;";
        cmd.Parameters.AddWithValue("@id", roleId);
        cmd.ExecuteNonQuery();
    }

    public static List<(string Code, string Description)> GetAllPermissionDefs()
        => AuthDatabase.AllPermissions.ToList();

    // Replaces the role's permission set with exactly the given codes.
    public static void SetRolePermissions(int roleId, IEnumerable<string> permissionCodes)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM dbo.RolePermissions WHERE RoleId = @r;";
                del.Parameters.AddWithValue("@r", roleId);
                del.ExecuteNonQuery();
            }
            foreach (var code in permissionCodes)
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = @"
INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT @r, PermissionId FROM dbo.Permissions WHERE PermissionCode = @c;";
                ins.Parameters.AddWithValue("@r", roleId);
                ins.Parameters.AddWithValue("@c", code);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }
}

// Who's currently logged in, for the lifetime of this run, plus a cached
// permission set (loaded once at login — a role change takes effect on the
// affected user's NEXT login, same as most desktop apps).
public static class Session
{
    public static UserAccount? CurrentUser { get; private set; }
    private static HashSet<string> _permissions = new();

    public static void SignIn(UserAccount user)
    {
        CurrentUser = user;
        _permissions = AuthRepository.GetPermissionsForRole(user.RoleId);
    }

    public static bool Has(string permissionCode) => _permissions.Contains(permissionCode);
}
