using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Data;

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

        private List<LineageObject> _lineage_columns = new List<LineageObject>();

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

        /// <summary>
        /// Retrieve a child with the specific name
        /// </summary>
        /// <param name="objectname"></param>
        public SsisObject GetChildByTypeAndAttr(string objectname, string attribute, string value)
        {
            return (from SsisObject o in Children 
                    where (o.DtsObjectType == objectname) 
                    && (o.Attributes[attribute] == value) 
                    select o).FirstOrDefault();
        }
        #endregion

        #region Translate this object into C# code
        /// <summary>
        /// Produce this variable to the current stream
        /// </summary>
        /// <param name="indent_depth"></param>
        /// <param name="as_global"></param>
        /// <param name="sw"></param>
        internal void EmitVariable(string indent, bool as_global, StreamWriter sw)
        {
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
        internal void EmitFunction(string indent, StreamWriter sw)
        {
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
                    childobj.EmitVariable(indent + "    ", false, sw);
                } else if (o.DtsObjectType == "DTS:Executable") {
                    childobj.EmitFunctionCall(indent + "    ", sw);
                } else if (childobj.DtsObjectType == "SQLTask:SqlTaskData") {
                    childobj.EmitSqlStatement(indent + "    ", sw);

                // TODO: Handle "pipeline" objects
                } else if (childobj.DtsObjectType == "pipeline") {
                    childobj.EmitPipeline(indent + "    ", sw);
                } else {
                    Console.WriteLine("Help!");
                }
            }

            // End of function
            sw.WriteLine("{0}}}", indent);

            // Now emit any other functions that are chained into this
            foreach (SsisObject o in Children) {
                if (o.DtsObjectType == "DTS:Executable") {
                    o.EmitFunction(indent, sw);
                }
            }
        }

        private void EmitPipeline(string indent, StreamWriter sw)
        {
            // Find the component container
            var component_container = (from c in this.Children where c.DtsObjectType == "components" select c).FirstOrDefault();
            if (component_container == null) return;

            // Iterate through all child components
            foreach (SsisObject child in component_container.Children) {
                string componentClassId = child.Attributes["componentClassID"];

                // Put in a comment for each component
                sw.WriteLine(@"{0}// {1}", indent, child.Attributes["name"]);

                // What type of component is this?  Is it a reader?
                if (componentClassId == "{BCEFE59B-6819-47F7-A125-63753B33ABB7}") {
                    child.EmitPipelineReader(indent, sw);
                    _lineage_columns.AddRange(child._lineage_columns);

                // Is it a transform?
                } else if (componentClassId == "{BD06A22E-BC69-4AF7-A69B-C44C2EF684BB}") {
                    child.EmitPipelineTransform(indent, sw);
                    _lineage_columns.AddRange(child._lineage_columns);

                // Is it a save?
                } else if (componentClassId == "{5A0B62E8-D91D-49F5-94A5-7BE58DE508F0}") {
                    child._lineage_columns = this._lineage_columns;
                    child.EmitPipelineWriter(indent, sw);
                }
            }
        }

        private void EmitPipelineWriter(string indent, StreamWriter sw)
        {
            // Get the connection string GUID: it's this.connections.connection
            string connstr = GetConnectionStringName(this.GetChildByType("connections").GetChildByType("connection").Attributes["connectionManagerID"]);

            // It's our problem to produce the SQL statement, because this writer uses calculated data!
            StringBuilder sql = new StringBuilder();
            StringBuilder colnames = new StringBuilder();
            StringBuilder varnames = new StringBuilder();
            StringBuilder paramsetup = new StringBuilder();

            // Retrieve the names of the columns we're inserting
            SsisObject metadata = this.GetChildByType("inputs").GetChildByType("input").GetChildByType("externalMetadataColumns");
            SsisObject columns = this.GetChildByType("inputs").GetChildByType("input").GetChildByType("inputColumns");

            // Okay, let's produce the columns we're inserting
            foreach (SsisObject column in columns.Children) {
                SsisObject mdcol = metadata.GetChildByTypeAndAttr("externalMetadataColumn", "id", column.Attributes["externalMetadataColumnId"]);

                // List of columns in the insert
                colnames.Append(mdcol.Attributes["name"]);
                colnames.Append(", ");

                // List of parameter names in the values clause
                varnames.Append("@");
                varnames.Append(mdcol.Attributes["name"]);
                varnames.Append(", ");

                // Find the source column in our lineage data
                string lineageId = column.Attributes["lineageId"];
                LineageObject lo = (from l in _lineage_columns where l.LineageId == lineageId select l).FirstOrDefault();

                // Parameter setup instructions
                if (lo == null) {
                    Console.WriteLine("Help!");
                } else {
                    paramsetup.AppendFormat(@"{0}            cmd.AddWithValue(""@{1}"",{2}.Rows[row][{3}]);
", indent, mdcol.Attributes["name"], lo.DataTableName, lo.DataTableColumn);
                }
            }
            colnames.Length -= 2;
            varnames.Length -= 2;

            // Produce the SQL statement
            sql.Append("INSERT INTO ");
            sql.Append(GetChildByType("properties").GetChildByTypeAndAttr("property", "name", "OpenRowset").ContentValue);
            sql.Append(" (");
            sql.Append(colnames.ToString());
            sql.Append(") VALUES ");
            sql.Append(varnames.ToString());

            // Produce a data set that we're going to process - name it after ourselves
            sw.WriteLine(@"{0}DataTable component{1} = new DataTable();", indent, this.Attributes["id"]);

            // Write the using clause for the connection
            sw.WriteLine(@"{0}using (var conn = new SqlConnection(ConfigurationManager.AppSettings[""{1}""]])) {{", indent, connstr);
            sw.WriteLine(@"{0}    conn.Open();", indent);
            sw.WriteLine(@"{0}    string sql = @""{1}"";", indent, sql.ToString().Replace("\"", "\"\"").Trim());

            // TODO: SQL Parameters should go in here

            // This is the laziest possible way to do this insert - may want to improve it later
            sw.WriteLine(@"{0}    for (int row = 0; row < row_count; row++) {{", indent);
            sw.WriteLine(@"{0}        using (var cmd = new SqlCommand(sql, conn)) {{", indent);
            sw.WriteLine(paramsetup);
            sw.WriteLine(@"{0}            cmd.ExecuteNonQuery();", indent);
            sw.WriteLine(@"{0}        }}", indent);
            sw.WriteLine(@"{0}    }}", indent);
            sw.WriteLine(@"{0}}}", indent);
        }

        private void EmitPipelineTransform(string indent, StreamWriter sw)
        {
            // Create a new datatable
            sw.WriteLine(@"{0}DataTable component{1} = new DataTable();", indent, this.Attributes["id"]);

            // Add the columns we're generating
            int i = 0;
            foreach (SsisObject outcol in this.GetChildByType("outputs").GetChildByTypeAndAttr("output", "isErrorOut", "false").GetChildByType("outputColumns").Children) {
                LineageObject lo = new LineageObject(outcol, this);
                lo.DataTableColumn = i;
                i++;
                _lineage_columns.Add(lo);

                // Print out this column
                sw.WriteLine(@"{0}component{1}.Columns.Add(new DataColumn(""{2}"", typeof({3})));", indent, this.Attributes["id"], outcol.Attributes["name"], outcol.Attributes["dataType"]);
                DataTable dt = new DataTable();
            }

            // Populate these columns
            sw.WriteLine(@"{0}for (int row = 0; row < row_count; row++) {{", indent);
            sw.WriteLine(@"{0}    // TODO: Transform the columns here", indent);
            sw.WriteLine(@"{0}}}", indent);
        }

        private void EmitPipelineReader(string indent, StreamWriter sw)
        {
            // Get the connection string GUID: it's this.connections.connection
            string connstr = GetConnectionStringName(this.GetChildByType("connections").GetChildByType("connection").Attributes["connectionManagerID"]);

            // Get the SQL statement
            string sql = this.GetChildByType("properties").GetChildByTypeAndAttr("property", "name", "SqlCommand").ContentValue;
            if (sql == null) sql = "COULD NOT FIND SQL STATEMENT";

            // Produce a data set that we're going to process - name it after ourselves
            sw.WriteLine(@"{0}DataTable component{1} = new DataTable();", indent, this.Attributes["id"]);

            // Keep track of the lineage of all of our output columns 
            // TODO: Handle error output columns
            int i = 0;
            foreach (SsisObject outcol in this.GetChildByType("outputs").GetChildByTypeAndAttr("output", "isErrorOut", "false").GetChildByType("outputColumns").Children) {
                LineageObject lo = new LineageObject(outcol, this);
                lo.DataTableColumn = i;
                i++;
                _lineage_columns.Add(lo);
            }

            // Write the using clause for the connection
            sw.WriteLine(@"{0}using (var conn = new SqlConnection(ConfigurationManager.AppSettings[""{1}""]])) {{", indent, connstr);
            sw.WriteLine(@"{0}    conn.Open();", indent);
            sw.WriteLine(@"{0}    string sql = @""{1}"";", indent, sql.Replace("\"", "\"\"").Trim());
            sw.WriteLine(@"{0}    using (var cmd = new SqlCommand(sql, conn)) {{", indent);
            sw.WriteLine(@"{0}        SqlDataReader dr = cmd.ExecuteReader();", indent);
            sw.WriteLine(@"{0}        component{1}.Load(dr);", indent, this.Attributes["id"]);
            sw.WriteLine(@"{0}        dr.Close();", indent);
            sw.WriteLine(@"{0}    }}", indent);
            sw.WriteLine(@"{0}}}", indent);

            // Set our row count
            sw.WriteLine(@"{0}row_count = component{1}.Rows.Count;", indent, this.Attributes["id"]);
        }

        /// <summary>
        /// Write out a function call
        /// </summary>
        /// <param name="indent"></param>
        /// <param name="sw"></param>
        private void EmitFunctionCall(string indent, StreamWriter sw)
        {
            sw.WriteLine(String.Format(@"{0}{1}();", indent, FixFunctionName(DtsObjectName)));
        }

        /// <summary>
        /// Write out an SQL statement
        /// </summary>
        /// <param name="indent_depth"></param>
        /// <param name="sw"></param>
        private void EmitSqlStatement(string indent, StreamWriter sw)
        {
            // Retrieve the connection string object
            string connstr = GetConnectionStringName(Attributes["SQLTask:Connection"]);

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
            sw.WriteLine(@"{0}        dr.Close();", indent);
            sw.WriteLine(@"{0}    }}", indent);
            sw.WriteLine(@"{0}}}", indent);
        }

        /// <summary>
        /// Get the connection string name when given a GUID
        /// </summary>
        /// <param name="conn_guid_str"></param>
        /// <returns></returns>
        private string GetConnectionStringName(string conn_guid_str)
        {
            SsisObject connobj = null;
            _guid_lookup.TryGetValue(Guid.Parse(conn_guid_str), out connobj);
            string connstr = connobj.DtsObjectName;
            return connstr;
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
