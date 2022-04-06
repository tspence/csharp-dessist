using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Xml;
using System.IO;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace SSMS-Export-to-Excel
{
    public static class Extensions
    {
        /// <summary>
        /// Converts a connection string in OLEDB format to one in ADO.NET format
        /// </summary>
        /// <param name="s">The connection string to change</param>
        public static string FixupOleDb(this string s)
        {
            int p = s.IndexOf("Provider=", StringComparison.CurrentCultureIgnoreCase);
            if (p >= 0) {
                int p2 = s.IndexOf(';', p + 1);
                return s.Substring(0, p) + s.Substring(p2 + 1);
            }
            return s;
        }
    }

    public class Program
    {
        public static RecursiveTimeLog timer = new RecursiveTimeLog();


        public static List<string> CreatedTableParams = new List<string>();
        public static void CreateTableParamType(string tableparam, SqlConnection conn)
        {
            // Only do this once each time the program executes
            if (!CreatedTableParams.Contains(tableparam)) {
                CreatedTableParams.Add(tableparam);

                // Now create this table parameter using the provided connection
                string table_param_create_sql = Resource1.ResourceManager.GetString(tableparam);
                using (SqlCommand cmd = new SqlCommand(table_param_create_sql, conn)) {
                    cmd.ExecuteNonQuery();
                }
            }
        }


        /// <summary>
        /// Main Function
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
			Preparation_SQL_Task();
            Console.WriteLine(timer.GetTimings());
        }





        public static void Preparation_SQL_Task()
        {
            timer.Enter("Preparation_SQL_Task");
            // These calls have no dependencies
            // ImportError: I don't understand the database connection type EXCEL
            Console.WriteLine("{0} SQL: preparation_sql_task", DateTime.Now);

            using (var conn = new Connection(ConfigurationManager.AppSettings["DestinationConnectionExcel"])) {
                conn.Open();

                // This SQL statement is a compound statement that must be run from the SQL Management object
                ServerConnection svrconn = new ServerConnection(conn);
                Server server = new Server(svrconn);
                server.ConnectionContext.SqlExecutionModes = SqlExecutionModes.CaptureSql;
                server.ConnectionContext.ExecuteNonQuery(Resource1.preparation_sql_task);
                foreach (string s in server.ConnectionContext.CapturedSql.Text) {
                    using (var cmd = new Command(s, conn)) {
                        cmd.CommandTimeout = 0;
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            timer.Leave();
        }

        public static void Data_Flow_Task()
        {
            timer.Enter("Data_Flow_Task");
            // ImportError: I don't yet know how to handle DTS.Pipeline
            timer.Leave();
        }
#endregion
    }
}

