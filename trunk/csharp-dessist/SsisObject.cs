using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace csharp_dessist
{
    public class SsisObject
    {
        private static Dictionary<Guid, SsisObject> _guid_lookup = new Dictionary<Guid, SsisObject>();

        /// <summary>
        /// The XML node type of this object
        /// </summary>
        public string DtsObjectType;

        /// <summary>
        /// The human readable name of this object
        /// </summary>
        public string DtsObjectName;

        /// <summary>
        /// A user-readable explanation of what this is
        /// </summary>
        public string Description;

        /// <summary>
        /// The GUID for this object
        /// </summary>
        public Guid DtsId;

        /// <summary>
        /// Attributes, if any
        /// </summary>
        public Dictionary<string, string> Attributes = new Dictionary<string, string>();

        /// <summary>
        /// All the properties defined in the SSIS
        /// </summary>
        public Dictionary<string, string> Properties = new Dictionary<string, string>();

        /// <summary>
        /// List of child elements in SSIS
        /// </summary>
        public List<SsisObject> Children = new List<SsisObject>();

        /// <summary>
        /// Save the content value of a complex object
        /// </summary>
        public string ContentValue;

        #region Shortcuts
        /// <summary>
        /// Set a property
        /// </summary>
        /// <param name="prop_name"></param>
        /// <param name="prop_value"></param>
        public void SetProperty(string prop_name, string prop_value)
        {
            if (prop_name == "ObjectName") {
                DtsObjectName = prop_value;
            } else if (prop_name == "DTSID") {
                DtsId = Guid.Parse(prop_value);
                _guid_lookup[DtsId] = this;
            } else if (prop_name == "Description") {
                Description = prop_value;
            } else {
                Properties[prop_name] = prop_value;
            }
        }

        /// <summary>
        /// Retrieve a child with the specific name
        /// </summary>
        /// <param name="objectname"></param>
        public SsisObject GetChildByType(string objectname)
        {
            return (from SsisObject o in Children where o.DtsObjectType == objectname select o).FirstOrDefault();
        }
        #endregion

        #region Translate this object into C# code
        /// <summary>
        /// Produce this variable to the current stream
        /// </summary>
        /// <param name="indent_depth"></param>
        /// <param name="as_global"></param>
        /// <param name="sw"></param>
        internal void EmitVariable(int indent_depth, bool as_global, StreamWriter sw)
        {
            string indent = new String(' ', indent_depth);

            if (as_global) {
                if (!String.IsNullOrEmpty(Description)) {
                    sw.WriteLine();
                    sw.WriteLine("{0}/// <summary>", indent);
                    sw.WriteLine("{0}/// {1}", indent, Description);
                    sw.WriteLine("{0}/// </summary>", indent);
                }
                sw.WriteLine(String.Format(@"{0}public string {1} = ""{2}"";", indent, DtsObjectName, ContentValue));
            } else {
                sw.WriteLine(String.Format(@"{0}string {1} = ""{2}"";", indent, DtsObjectName, ContentValue));
            }
        }

        /// <summary>
        /// Produce this variable to the current stream
        /// </summary>
        /// <param name="indent_depth"></param>
        /// <param name="as_global"></param>
        /// <param name="sw"></param>
        internal void EmitFunction(int indent_depth, StreamWriter sw)
        {
            string indent = new String(' ', indent_depth);

            // Header and comments
            sw.WriteLine();
            if (!String.IsNullOrEmpty(Description)) {
                sw.WriteLine("{0}/// <summary>", indent);
                sw.WriteLine("{0}/// {1}", indent, Description);
                sw.WriteLine("{0}/// </summary>", indent);
            }

            // Function intro
            sw.WriteLine(String.Format("{0}public static void {1}()\n        {{\n", indent, FixFunctionName(DtsObjectName)));

            // TODO: Is there an exception handler?  How many types of event handlers are there?
            // TODO: Check precedence constraints
            // TODO: Create a general purpose lookup of DTSID objects

            // Function body
            foreach (SsisObject o in Children) {

                // Is this a dummy "Object Data" thing?  If so ignore it and delve deeper
                SsisObject childobj = o;
                if (childobj.DtsObjectType == "DTS:ObjectData") {
                    childobj = childobj.Children[0];
                }

                // For variables, emit them within this function
                if (childobj.DtsObjectType == "DTS:Variable") {
                    childobj.EmitVariable(indent_depth + 4, false, sw);
                } else if (o.DtsObjectType == "DTS:Executable") {
                    childobj.EmitFunctionCall(indent_depth + 4, sw);
                } else if (childobj.DtsObjectType == "SQLTask:SqlTaskData") {
                    childobj.EmitSqlStatement(indent_depth + 4, sw);

                // TODO: Handle "pipeline" objects
                } else if (childobj.DtsObjectType == "pipeline") {
                    childobj.EmitPipeline(indent_depth + 4, sw);
                } else {
                    Console.WriteLine("Help!");
                }
            }

            // End of function
            sw.WriteLine("{0}}}", indent);

            // Now emit any other functions that are chained into this
            foreach (SsisObject o in Children) {
                if (o.DtsObjectType == "DTS:Executable") {
                    o.EmitFunction(indent_depth, sw);
                }
            }
        }

        private void EmitPipeline(int p, StreamWriter sw)
        {
            //TODO: Read data in from the first component and write it out to the second component
        }

        /// <summary>
        /// Write out a function call
        /// </summary>
        /// <param name="indent"></param>
        /// <param name="sw"></param>
        private void EmitFunctionCall(int indent_depth, StreamWriter sw)
        {
            string indent = new String(' ', indent_depth);

            sw.WriteLine(String.Format(@"{0}{1}();", indent, FixFunctionName(DtsObjectName)));
        }

        /// <summary>
        /// Write out an SQL statement
        /// </summary>
        /// <param name="indent_depth"></param>
        /// <param name="sw"></param>
        private void EmitSqlStatement(int indent_depth, StreamWriter sw)
        {
            string indent = new String(' ', indent_depth);

            // Retrieve the connection string object
            string conn_guid_str = null;
            Attributes.TryGetValue("SQLTask:Connection", out conn_guid_str);
            SsisObject connobj = null;
            _guid_lookup.TryGetValue(Guid.Parse(conn_guid_str), out connobj);
            string connstr = connobj.DtsObjectName;

            // Retrieve the SQL String
            string sql = Attributes["SQLTask:SqlStatementSource"];

            // Write the using clause for the connection
            sw.WriteLine(@"{0}using (var conn = new SqlConnection(ConfigurationManager.AppSettings[""{1}""]])) {{", indent, connstr);
            sw.WriteLine(@"{0}    conn.Open();", indent);
            sw.WriteLine(@"{0}    string sql = @""{1}"";", indent, sql.Replace("\"","\"\"").Trim());

            // TODO: SQL Parameters should go in here

            sw.WriteLine(@"{0}    using (var cmd = new SqlCommand(sql, conn)) {{", indent);
            sw.WriteLine(@"{0}        SqlDataReader dr = cmd.ExecuteReader();", indent);
            sw.WriteLine(@"{0}        DataSet ds = new DataSet();", indent);
            sw.WriteLine(@"{0}        ds.Load(dr);", indent);
            sw.WriteLine(@"{0}    }}", indent);
            sw.WriteLine(@"{0}}}", indent);
        }
        #endregion

        #region Helper functions
        private static string FixFunctionName(string str)
        {
            Regex rgx = new Regex("[^a-zA-Z0-9]");
            return rgx.Replace(str, "_");
        }
        #endregion
    }
}
