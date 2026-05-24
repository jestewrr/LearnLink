using System;
using Microsoft.Data.SqlClient;

class Program
{
    static void Main()
    {
        string connString = "Server=(localdb)\\MSSQLLocalDB;Database=DB_LearnLink;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
        
        string[] potentialPaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "LearnLink", "appsettings.Development.local.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "LearnLink", "appsettings.Development.local.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "LearnLink", "appsettings.Development.local.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "LearnLink", "appsettings.Development.local.json"),
            "appsettings.Development.local.json"
        };

        foreach (var path in potentialPaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    string content = File.ReadAllText(path);
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"""DefaultConnection""\s*:\s*""([^""]+)""");
                    if (match.Success)
                    {
                        connString = match.Groups[1].Value.Replace("\\\\", "\\");
                        Console.WriteLine($"Loaded connection string from: {Path.GetFullPath(path)}");
                        break;
                    }
                }
                catch
                {
                    // Ignore and try next path
                }
            }
        }

        try
        {
            Console.WriteLine($"Connecting to DB with connection string: {connString}");
            using (var conn = new SqlConnection(connString))
            {
                conn.Open();
                Console.WriteLine("Connected successfully!\n");

                using (var cmd = conn.CreateCommand())
                {
                    // 1) Check BackupRecords table schema
                    Console.WriteLine("=== BackupRecords columns ===");
                    cmd.CommandText = @"
                        SELECT c.name, t.name AS type, c.max_length, c.is_nullable, c.is_identity
                        FROM sys.columns c
                        JOIN sys.types t ON c.user_type_id = t.user_type_id
                        WHERE c.object_id = OBJECT_ID('BackupRecords')
                        ORDER BY c.column_id";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Console.WriteLine($"  {reader[0]} ({reader[1]}, len={reader[2]}, nullable={reader[3]}, identity={reader[4]})");
                        }
                    }

                    // 2) Check BackupRecords FK constraints
                    Console.WriteLine("\n=== BackupRecords FK constraints ===");
                    cmd.CommandText = @"
                        SELECT fk.name, fk.delete_referential_action_desc
                        FROM sys.foreign_keys fk
                        WHERE fk.parent_object_id = OBJECT_ID('BackupRecords')
                           OR fk.referenced_object_id = OBJECT_ID('BackupRecords')";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Console.WriteLine($"  {reader[0]} -> OnDelete={reader[1]}");
                        }
                    }

                    // 3) Try inserting a test backup record
                    Console.WriteLine("\n=== Test INSERT into BackupRecords ===");
                    cmd.CommandText = @"
                        INSERT INTO BackupRecords (BackupType, Status, CreatedAt, TotalSizeMb, ProgressPercent)
                        VALUES ('TestManual', 'Testing', GETUTCDATE(), 0.0, 0);
                        SELECT SCOPE_IDENTITY();";
                    var newId = cmd.ExecuteScalar();
                    Console.WriteLine($"  SUCCESS - Inserted BackupRecord Id={newId}");

                    // Clean up test record
                    cmd.CommandText = "DELETE FROM BackupRecords WHERE Id=@delId";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@delId", newId);
                    cmd.ExecuteNonQuery();
                    Console.WriteLine($"  Cleaned up test record Id={newId}");
                    cmd.Parameters.Clear();

                    // 4) Check all FK constraints referencing AspNetUsers (to find what blocks deletion)
                    Console.WriteLine("\n=== FK constraints referencing AspNetUsers ===");
                    cmd.CommandText = @"
                        SELECT 
                            fk.name AS FK_Name,
                            OBJECT_NAME(fk.parent_object_id) AS ChildTable,
                            fk.delete_referential_action_desc AS OnDelete
                        FROM sys.foreign_keys fk
                        WHERE fk.referenced_object_id = OBJECT_ID('AspNetUsers')
                        ORDER BY ChildTable";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Console.WriteLine($"  {reader[0]} | Table={reader[1]} | OnDelete={reader[2]}");
                        }
                    }

                    // 5) Check BackupPolicies table  
                    Console.WriteLine("\n=== BackupPolicies rows ===");
                    cmd.CommandText = "SELECT COUNT(*) FROM BackupPolicies";
                    Console.WriteLine($"  Count: {cmd.ExecuteScalar()}");

                    // 6) Check if BackupItems table exists and its schema
                    Console.WriteLine("\n=== BackupItems columns ===");
                    cmd.CommandText = @"
                        SELECT c.name, t.name AS type, c.max_length, c.is_nullable
                        FROM sys.columns c
                        JOIN sys.types t ON c.user_type_id = t.user_type_id
                        WHERE c.object_id = OBJECT_ID('BackupItems')
                        ORDER BY c.column_id";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Console.WriteLine($"  {reader[0]} ({reader[1]}, len={reader[2]}, nullable={reader[3]})");
                        }
                    }

                    // 7) Check existing BackupRecords
                    Console.WriteLine("\n=== Existing BackupRecords ===");
                    cmd.CommandText = "SELECT Id, BackupType, Status, CreatedAt, TriggeredByUserId, TotalSizeMb, ProgressPercent FROM BackupRecords ORDER BY CreatedAt DESC";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Console.WriteLine($"  Id={reader[0]} Type={reader[1]} Status={reader[2]} Created={reader[3]} UserId={reader[4]} Size={reader[5]} Progress={reader[6]}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"INNER ERROR: {ex.InnerException.Message}");
            }
            Console.WriteLine(ex.StackTrace);
        }
    }
}
