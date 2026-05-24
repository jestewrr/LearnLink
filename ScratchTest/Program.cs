using System;
using Microsoft.Data.SqlClient;

class Program
{
    static void Main()
    {
        string connString = "Server=db41134.databaseasp.net;Database=db41134;User Id=db41134;Password=h@9BN8c_6-Rp;Encrypt=True;TrustServerCertificate=True;Connection Timeout=30;";
        try
        {
            Console.WriteLine("Connecting to DB...");
            using (var conn = new SqlConnection(connString))
            {
                conn.Open();
                Console.WriteLine("Connected successfully!");

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM AuditLogs";
                    int count = (int)cmd.ExecuteScalar();
                    Console.WriteLine($"AuditLogs count: {count}");

                    cmd.CommandText = "SELECT TOP 10 Timestamp, UserEmail, Action, Status, Details, SchoolId FROM AuditLogs ORDER BY Timestamp DESC";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Console.WriteLine($"{reader[0]} | {reader[1]} | {reader[2]} | {reader[3]} | {reader[4]} | {reader[5]}");
                        }
                    }

                    cmd.CommandText = "SELECT COUNT(*) FROM UserActivityLogs";
                    int count2 = (int)cmd.ExecuteScalar();
                    Console.WriteLine($"UserActivityLogs count: {count2}");

                    cmd.CommandText = "SELECT TOP 10 ActivityDate, UserId, ActivityType, TargetTitle FROM UserActivityLogs ORDER BY ActivityDate DESC";
                    using (var reader2 = cmd.ExecuteReader())
                    {
                        while (reader2.Read())
                        {
                            Console.WriteLine($"{reader2[0]} | {reader2[1]} | {reader2[2]} | {reader2[3]}");
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
