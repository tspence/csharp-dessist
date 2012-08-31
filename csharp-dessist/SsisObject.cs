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
        /// <summary>
        /// The XML node type of this object
        /// </summary>
        public string DtsObjectType;

        /// <summary>
        /// The human readable name of this object
        /// </summary>
        public string DtsObjectName;
        private string _FunctionName;
        private string _FolderName;

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
        public SsisObject Parent = null;

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
            // Figure out what type of a variable we are
            string dtstype = this.GetChildByType("DTS:VariableValue").Attributes["DTS:DataType"];
            string csharptype = null;

            // Integer
            if (dtstype == "3") {
                csharptype = "int";
            } else if (dtstype == "8") {
                csharptype = "string";
            } else if (dtstype == "13") {
                csharptype = "DataTable";
            } else if (dtstype == "2") {
                csharptype = "byte";
            } else if (dtstype == "11") {
                csharptype = "bool";
            } else if (dtstype == "20") {
                csharptype = "long";
            } else if (dtstype == "7") {
                csharptype = "DateTime";
            } else {
                Console.WriteLine("Help!  I don't understand DTS type " + dtstype);
            }

            if (as_global) {
                if (!String.IsNullOrEmpty(Description)) {
                    sw.WriteLine();
                    sw.WriteLine("{0}/// <summary>", indent);
                    sw.WriteLine("{0}/// {1}", indent, Description);
                    sw.WriteLine("{0}/// </summary>", indent);
                }
                sw.WriteLine(String.Format(@"{0}public {3} {1} = ""{2}"";", indent, DtsObjectName, ContentValue, csharptype));
            } else {
                sw.WriteLine(String.Format(@"{0}{3} {1} = ""{2}"";", indent, DtsObjectName, ContentValue, csharptype));
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
            sw.WriteLine(String.Format("{0}public static void {1}(){2}        {{", indent, GetFunctionName(), Environment.NewLine));

            // What type of executable are we?  Let's check if special handling is required
            string exec_type = Attributes["DTS:ExecutableType"];

            // Child script project - Emit it as a sub-project within the greater solution!
            if (exec_type.StartsWith("Microsoft.SqlServer.Dts.Tasks.ScriptTask.ScriptTask")) {
                ProjectWriter.EmitScriptProject(this, indent + "    ", sw);

            // Basic SQL command
            } else if (exec_type.StartsWith("Microsoft.SqlServer.Dts.Tasks.ExecuteSQLTask.ExecuteSQLTask")) {
                // Already handled within - it's just a single SQL statement

            // Something I don't yet understand
            } else {
                Console.WriteLine("Help!  I don't yet know how to handle " + exec_type);
            }

            // TODO: Is there an exception handler?  How many types of event handlers are there?
            // TODO: Check precedence constraints
            // TODO: Create a general purpose lookup of DTSID objects

            // Figure out all the precedence constraints within our child objects
            List<PrecedenceData> list = new List<PrecedenceData>();
            foreach (SsisObject o in Children) {
                if (o.DtsObjectType == "DTS:PrecedenceConstraint") {
                    list.Add(new PrecedenceData(o));
                }
            }

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
                } else if (childobj.DtsObjectType == "DTS:PrecedenceConstraint") {
                    // ignore it - it's already been handled
                } else if (childobj.DtsObjectType == "DTS:LoggingOptions") {
                    // Ignore it - I can't figure out any useful information on this object
                } else {
                    Console.WriteLine("Help!  I don't yet know how to handle " + childobj.DtsObjectType);
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

            // Produce a "row count" variable we can use
            sw.WriteLine(@"{0}int row_count = 0;", indent);

            // Iterate through all child components
            foreach (SsisObject child in component_container.Children) {
                string componentClassId = child.Attributes["componentClassID"];

                // Put in a comment for each component
                sw.WriteLine();
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
            string conn_guid = this.GetChildByType("connections").GetChildByType("connection").Attributes["connectionManagerID"];
            string connstr = ConnectionWriter.GetConnectionStringName(conn_guid);
            string connprefix = ConnectionWriter.GetConnectionStringPrefix(conn_guid);

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
                    Console.WriteLine("Help!  I couldn't find lineage column " + lineageId);
                } else {
                    paramsetup.AppendFormat(@"{0}            cmd.Parameters.AddWithValue(""@{1}"",{2}.Rows[row][{3}]);
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
            string sql_resource_name = ProjectWriter.AddSqlResource(GetParentDtsName() + "_WritePipe", sql.ToString());

            // Produce a data set that we're going to process - name it after ourselves
            sw.WriteLine(@"{0}DataTable component{1} = new DataTable();", indent, this.Attributes["id"]);

            // Write the using clause for the connection
            sw.WriteLine(@"{0}using (var conn = new {2}Connection(ConfigurationManager.AppSettings[""{1}""])) {{", indent, connstr, connprefix);
            sw.WriteLine(@"{0}    conn.Open();", indent);

            // TODO: SQL Parameters should go in here

            // This is the laziest possible way to do this insert - may want to improve it later
            sw.WriteLine(@"{0}    for (int row = 0; row < row_count; row++) {{", indent);
            sw.WriteLine(@"{0}        using (var cmd = new {2}Command(Resource1.{1}, conn)) {{", indent, sql_resource_name, connprefix);
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
                sw.WriteLine(@"{0}component{1}.Columns.Add(new DataColumn(""{2}"", typeof({3})));", indent, this.Attributes["id"], outcol.Attributes["name"], LookupSsisTypeName(outcol.Attributes["dataType"]));
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
            string conn_guid = this.GetChildByType("connections").GetChildByType("connection").Attributes["connectionManagerID"];
            string connstr = ConnectionWriter.GetConnectionStringName(conn_guid);
            string connprefix = ConnectionWriter.GetConnectionStringPrefix(conn_guid);

            // Get the SQL statement
            string sql = this.GetChildByType("properties").GetChildByTypeAndAttr("property", "name", "SqlCommand").ContentValue;
            if (sql == null) sql = "COULD NOT FIND SQL STATEMENT";
            string sql_resource_name = ProjectWriter.AddSqlResource(GetParentDtsName() + "_ReadPipe", sql);

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
            sw.WriteLine(@"{0}using (var conn = new {2}Connection(ConfigurationManager.AppSettings[""{1}""])) {{", indent, connstr, connprefix);
            sw.WriteLine(@"{0}    conn.Open();", indent);
            sw.WriteLine(@"{0}    using (var cmd = new {2}Command(Resource1.{1}, conn)) {{", indent, sql_resource_name, connprefix);
            sw.WriteLine(@"{0}        {1}DataReader dr = cmd.ExecuteReader();", indent, connprefix);
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
            sw.WriteLine(String.Format(@"{0}{1}();", indent, GetFunctionName()));
        }

        /// <summary>
        /// Write out an SQL statement
        /// </summary>
        /// <param name="indent_depth"></param>
        /// <param name="sw"></param>
        private void EmitSqlStatement(string indent, StreamWriter sw)
        {
            // Retrieve the connection string object
            string conn_guid = Attributes["SQLTask:Connection"];
            string connstr = ConnectionWriter.GetConnectionStringName(conn_guid);
            string connprefix = ConnectionWriter.GetConnectionStringPrefix(conn_guid);

            // Retrieve the SQL String and put it in a resource
            string sql_attr_name = ProjectWriter.AddSqlResource(GetParentDtsName(), Attributes["SQLTask:SqlStatementSource"]);

            // Write the using clause for the connection
            sw.WriteLine(@"{0}DataTable dt = null;", indent, connstr);
            sw.WriteLine(@"", indent, connstr);
            sw.WriteLine(@"{0}using (var conn = new {2}Connection(ConfigurationManager.AppSettings[""{1}""])) {{", indent, connstr, connprefix);
            sw.WriteLine(@"{0}    conn.Open();", indent);

            // TODO: SQL Parameters should go in here

            sw.WriteLine(@"{0}    using (var cmd = new {2}Command(Resource1.{1}, conn)) {{", indent, sql_attr_name, connprefix);
            sw.WriteLine(@"{0}        {1}DataReader dr = cmd.ExecuteReader();", indent, connprefix);
            sw.WriteLine(@"{0}        dt = new DataTable();", indent);
            sw.WriteLine(@"{0}        dt.Load(dr);", indent);
            sw.WriteLine(@"{0}        dr.Close();", indent);
            sw.WriteLine(@"{0}    }}", indent);
            sw.WriteLine(@"{0}}}", indent);
        }

        private string GetParentDtsName()
        {
            SsisObject obj = this;
            while (obj != null && obj.DtsObjectName == null) {
                obj = obj.Parent;
            }
            if (obj == null) {
                return "Unnamed";
            } else {
                return obj.DtsObjectName;
            }
        }
        #endregion

        #region Helper functions
        private static List<string> _func_names = new List<string>();
        public string GetFunctionName()
        {
            if (_FunctionName == null) {
                Regex rgx = new Regex("[^a-zA-Z0-9]");
                string fn = rgx.Replace(GetParentDtsName(), "_");

                // Uniqueify!
                int i = 0;
                string newfn = fn;
                while (_func_names.Contains(newfn)) {
                    i++;
                    newfn = fn + "_" + i.ToString();
                }
                _FunctionName = newfn;
                _func_names.Add(_FunctionName);
            }
            return _FunctionName;
        }

        private static string LookupSsisTypeName(string p)
        {
            if (p == "i2") {
                return "System.Int16";
            } else if (p == "str") {
                return "System.String";
            } else {
                Console.WriteLine("Help!");
            }
            return null;
        }

        private static Dictionary<Guid, SsisObject> _guid_lookup = new Dictionary<Guid, SsisObject>();
        public static SsisObject GetObjectByGuid(Guid g)
        {
            return _guid_lookup[g];
        }

        private static List<string> _folder_names = new List<string>();
        public string GetFolderName()
        {
            if (_FolderName == null) {
                Regex rgx = new Regex("[^a-zA-Z0-9]");
                string fn = rgx.Replace(GetParentDtsName(), "");

                // Uniqueify!
                int i = 0;
                string newfn = fn;
                while (_folder_names.Contains(newfn)) {
                    i++;
                    newfn = fn + "_" + i.ToString();
                }
                _FolderName = newfn;
                _folder_names.Add(_FolderName);
            }
            return _FolderName;
        }
        #endregion
    }
}
