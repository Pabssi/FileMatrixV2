using System;
using System.Data;
using Microsoft.Data.SqlClient;

namespace SchemaCheck
{
    class Program
    {
        static void Main(string[] args)
        {
            string connectionString = "Server=(localdb)\\mssqllocaldb;Database=FileMatrix-Pabiran;Trusted_Connection=True;MultipleActiveResultSets=true";
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var schema = connection.GetSchema("Columns", new[] { null, null, "Documents" });
                Console.WriteLine("Columns in Documents table:");
                foreach (DataRow row in schema.Rows)
                {
                    Console.WriteLine($"- {row["COLUMN_NAME"]} ({row["DATA_TYPE"]})");
                }
            }
        }
    }
}
