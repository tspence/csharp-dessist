/*
 * 2012-2015 Ted Spence, http://tedspence.com
 * License: http://www.apache.org/licenses/LICENSE-2.0 
 * Home page: https://github.com/tspence/csharp-dessist
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Dessist
{
    public class ConnectionWriter
    {

        /// <summary>
        /// Write an app.config file with the specified settings
        /// </summary>
        /// <param name="connstrings"></param>
        public static void WriteAppConfig(IEnumerable<SsisObject> connstrings, string filename)
        {
            using (var sw = new StreamWriter(filename, false, Encoding.UTF8)) {

                // Write the header
                sw.WriteLine(@"<?xml version=""1.0"" encoding=""utf-8""?>");
                sw.WriteLine(@"<configuration>");
                sw.WriteLine(@"  <startup useLegacyV2RuntimeActivationPolicy=""true"">");
                sw.WriteLine(@"    <supportedRuntime version=""v4.0""/>");
                sw.WriteLine(@"  </startup>");
                sw.WriteLine(@"  <appSettings>");

                // Write each one in turn
                foreach (var connstr in connstrings) {
                    var s = "Not Found";
                    var v = connstr.GetChildByType("DTS:ObjectData");
                    if (v != null) {

                        // Look for a SQL Connection string
                        var v2 = v.GetChildByType("DTS:ConnectionManager");
                        if (v2 != null) {
                            v2.Properties.TryGetValue("ConnectionString", out s);

                            // If not, look for an SMTP connection string
                        } else {
                            v2 = v.GetChildByType("SmtpConnectionManager");
                            if (v2 != null) {
                                v2.Attributes.TryGetValue("ConnectionString", out s);
                            } else {
                                _project.Log("Missing SmtpConnectionManager value");
                            }
                        }
                    }
                    sw.WriteLine($"    <add key=\"{connstr.DtsObjectName}\" value=\"{s}\" />");

                    // Save to the lookup
                }

                // Write the footer
                sw.WriteLine("  </appSettings>");
                sw.WriteLine("</configuration>");
            }
        }

        /// <summary>
        /// Get the connection string name when given a GUID
        /// </summary>
        /// <param name="conn_guid_str"></param>
        /// <returns></returns>
        public static string GetConnectionStringName(string? conn_guid_str)
        {
            var connobj = _project.GetObjectByGuid(Guid.Parse(conn_guid_str));
            var connstr = "";
            if (connobj != null) {
                connstr = connobj.DtsObjectName;
            }
            return connstr;
        }

        /// <summary>
        /// Get the connection string name when given a GUID
        /// </summary>
        /// <param name="conn_guid_str"></param>
        /// <returns></returns>
        public static string GetConnectionStringPrefix(string? conn_guid_str)
        {
            SsisObject connobj = _project.GetObjectByGuid(Guid.Parse(conn_guid_str));
            if (connobj == null) {
                return "Sql";
            }
            string objecttype = connobj.Properties["CreationName"];
            if (objecttype.StartsWith("OLEDB")) {
                return "OleDb";
            } else if (objecttype.StartsWith("ADO.NET:System.Data.SqlClient.SqlConnection")) {
                return "Sql";
            } else {
                SourceWriter.Help(null, "I don't understand the database connection type " + objecttype);
            }
            return "";
        }
    }
}
