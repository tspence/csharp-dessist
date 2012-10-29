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

        #region Lineage Column Helpers
        private List<LineageObject> _lineage_columns = new List<LineageObject>();

        /// <summary>
        /// Shortcut to find a lineage object
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public LineageObject GetLineageObjectById(string id)
        {
            return (from LineageObject l in _lineage_columns where (l.LineageId == id) select l).FirstOrDefault();
        }

        #endregion
        private List<VariableData> _scope_variables = new List<VariableData>();

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
        internal VariableData EmitVariable(string indent, bool as_global)
        {
            VariableData vd = new VariableData(this, as_global);

            // Do we add comments for these variables?
            string privilege = "";
            if (as_global) {
                if (!String.IsNullOrEmpty(vd.Comment)) {
                    SourceWriter.WriteLine();
                    SourceWriter.WriteLine("{0}/// <summary>", indent);
                    SourceWriter.WriteLine("{0}/// {1}", indent, vd.Comment);
                    SourceWriter.WriteLine("{0}/// </summary>", indent);
                }
                privilege = "public static ";
            }

            // Write it out
            if (String.IsNullOrEmpty(vd.DefaultValue)) {
                SourceWriter.WriteLine(String.Format(@"{0}{3}{2} {1};", indent, vd.VariableName, vd.CSharpType, privilege));
            } else {
                SourceWriter.WriteLine(String.Format(@"{0}{4}{3} {1} = {2};", indent, vd.VariableName, vd.DefaultValue, vd.CSharpType, privilege));
            }

            // Keep track of variables so we can do type conversions in the future!
            _var_dict[vd.VariableName] = vd;
            return vd;
        }
        protected static Dictionary<string, VariableData> _var_dict = new Dictionary<string, VariableData>();

        /// <summary>
        /// Produce this variable to the current stream
        /// </summary>
        /// <param name="indent_depth"></param>
        /// <param name="as_global"></param>
        /// <param name="sw"></param>
        internal void EmitFunction(string indent, List<VariableData> scope_variables)
        {
            _scope_variables.AddRange(scope_variables);

            // Header and comments
            SourceWriter.WriteLine();
            if (!String.IsNullOrEmpty(Description)) {
                SourceWriter.WriteLine("{0}/// <summary>", indent);
                SourceWriter.WriteLine("{0}/// {1}", indent, Description);
                SourceWriter.WriteLine("{0}/// </summary>", indent);
            }

            // Function intro
            SourceWriter.WriteLine("{0}public static void {1}({2})", indent, GetFunctionName(), GetScopeVariables(true));
            SourceWriter.WriteLine("{0}{{", indent);
            SourceWriter.WriteLine(@"{0}    timer.Enter(""{1}"");", indent, GetFunctionName());

            // What type of executable are we?  Let's check if special handling is required
            string exec_type = Attributes["DTS:ExecutableType"];

            // Child script project - Emit it as a sub-project within the greater solution!
            if (exec_type.StartsWith("Microsoft.SqlServer.Dts.Tasks.ScriptTask.ScriptTask")) {
                ProjectWriter.EmitScriptProject(this, indent + "    ");

            // Basic SQL command
            } else if (exec_type.StartsWith("Microsoft.SqlServer.Dts.Tasks.ExecuteSQLTask.ExecuteSQLTask")) {
                this.EmitSqlTask(indent);

            // Basic "SEQUENCE" construct - just execute things in order!
            } else if (exec_type.StartsWith("STOCK:SEQUENCE")) {
                EmitChildObjects(indent);

            // Handle "FOR" and "FOREACH" loop types
            } else if (exec_type == "STOCK:FORLOOP") {
                this.EmitForLoop(indent + "    ");
            } else if (exec_type == "STOCK:FOREACHLOOP") {
                this.EmitForEachLoop(indent + "    ");
            } else if (exec_type == "SSIS.Pipeline.2") {
                this.EmitPipeline(indent + "    ");
            } else if (exec_type.StartsWith("Microsoft.SqlServer.Dts.Tasks.SendMailTask.SendMailTask")) {
                this.EmitSendMailTask(indent + "    ");

            // Something I don't yet understand
            } else {
                SourceWriter.Help(this, "I don't yet know how to handle " + exec_type);
            }

            // TODO: Is there an exception handler?  How many types of event handlers are there?

            // End of function
            SourceWriter.WriteLine("{0}    timer.Leave();", indent);
            SourceWriter.WriteLine("{0}}}", indent);

            // Now emit any other functions that are chained into this
            foreach (SsisObject o in Children) {
                if (o.DtsObjectType == "DTS:Executable") {
                    o.EmitFunction(indent, _scope_variables);
                }
            }
        }

        private void EmitSendMailTask(string indent)
        {
            // Navigate to our object data
            SsisObject mail = GetChildByType("DTS:ObjectData").GetChildByType("SendMailTask:SendMailTaskData");

            SourceWriter.WriteLine(@"{0}MailMessage message = new MailMessage();", indent);
            SourceWriter.WriteLine(@"{0}message.To.Add(""{1}"");", indent, mail.Attributes["SendMailTask:To"]);
            SourceWriter.WriteLine(@"{0}message.Subject = ""{1}"";", indent, mail.Attributes["SendMailTask:Subject"]);
            SourceWriter.WriteLine(@"{0}message.From = new MailAddress(""{1}"");", indent, mail.Attributes["SendMailTask:From"]);
            
            // Handle CC/BCC if available
            string addr = null;
            if (mail.Attributes.TryGetValue("SendMailTask:CC", out addr) && !String.IsNullOrEmpty(addr)) {
                SourceWriter.WriteLine(@"{0}message.CC.Add(""{1}"");", indent, addr);
            }
            if (mail.Attributes.TryGetValue("SendMailTask:BCC", out addr) && !String.IsNullOrEmpty(addr)) {
                SourceWriter.WriteLine(@"{0}message.Bcc.Add(""{1}"");", indent, addr);
            }

            // Process the message source
            string sourcetype = mail.Attributes["SendMailTask:MessageSourceType"];
            if (sourcetype == "Variable") {
                SourceWriter.WriteLine(@"{0}message.Body = {1};", indent, FixVariableName(mail.Attributes["SendMailTask:MessageSource"]));
            } else if (sourcetype == "DirectInput") {
                SourceWriter.WriteLine(@"{0}message.Body = @""{1}"";", indent, mail.Attributes["SendMailTask:MessageSource"].Replace("\"", "\"\""));
            } else {
                SourceWriter.Help(this, "I don't understand the SendMail message source type '" + sourcetype + "'");
            }

            // Get the SMTP configuration name
            SourceWriter.WriteLine(@"{0}using (var smtp = new SmtpClient(ConfigurationManager.AppSettings[""{1}""])) {{", indent, GetObjectByGuid(mail.Attributes["SendMailTask:SMTPServer"]).DtsObjectName);
            SourceWriter.WriteLine(@"{0}    smtp.Send(message);", indent);
            SourceWriter.WriteLine(@"{0}}}", indent);
        }

        private void EmitSqlTask(string indent)
        {
            EmitChildObjects(indent);
        }

        private void EmitChildObjects(string indent)
        {
            string newindent = indent + "    ";

            // To handle precedence data correctly, first make a list of encumbered children
            List<SsisObject> modified_children = new List<SsisObject>();
            modified_children.AddRange(Children);

            // Write comments for the precedence data - we'll eventually have to handle this
            List<PrecedenceData> precedence = new List<PrecedenceData>();
            foreach (SsisObject o in Children) {
                if (o.DtsObjectType == "DTS:PrecedenceConstraint") {
                    PrecedenceData pd = new PrecedenceData(o);

                    // Does this precedence data affect any children?  Find it and move it
                    var c = (from SsisObject obj in modified_children where obj.DtsId == pd.AfterGuid select obj).FirstOrDefault();
                    modified_children.Remove(c);

                    // Add it to the list
                    precedence.Add(pd);
                }
            }

            if (modified_children.Count > 0) {
                SourceWriter.WriteLine("{0}// These calls have no dependencies", newindent);

                // Function body
                foreach (SsisObject o in modified_children) {

                    // Are there any precedence triggers after this child?
                    PrecedenceChain(o, precedence, newindent);
                }
            }
        }

        private void PrecedenceChain(SsisObject prior_obj, List<PrecedenceData> precedence, string indent)
        {
            EmitOneChild(prior_obj, indent);

            // We just executed "prior_obj" - find what objects it causes to be triggered
            var triggered = (from PrecedenceData pd in precedence where pd.BeforeGuid == prior_obj.DtsId select pd);

            // Iterate through each of these
            foreach (PrecedenceData pd in triggered) {

                // Write a comment
                SourceWriter.WriteLine();
                SourceWriter.WriteLine("{0}// {1}", indent, pd.ToString());

                // Is there an expression?
                if (!String.IsNullOrEmpty(pd.Expression)) {
                    SourceWriter.WriteLine(@"{0}if ({1}) {{", indent, FixExpression("System.Boolean", _lineage_columns, pd.Expression, true));
                    PrecedenceChain(pd.Target, precedence, indent + "    ");
                    SourceWriter.WriteLine(@"{0}}}", indent);
                } else {
                    PrecedenceChain(pd.Target, precedence, indent);
                }
            }
        }

        private void EmitOneChild(SsisObject childobj, string newindent)
        {
            // Is this a dummy "Object Data" thing?  If so ignore it and delve deeper
            if (childobj.DtsObjectType == "DTS:ObjectData") {
                childobj = childobj.Children[0];
            }

            // For variables, emit them within this function
            if (childobj.DtsObjectType == "DTS:Variable") {
                _scope_variables.Add(childobj.EmitVariable(newindent, false));
            } else if (childobj.DtsObjectType == "DTS:Executable") {
                childobj.EmitFunctionCall(newindent, GetScopeVariables(false));
            } else if (childobj.DtsObjectType == "SQLTask:SqlTaskData") {
                childobj.EmitSqlStatement(newindent);

                // TODO: Handle "pipeline" objects
            } else if (childobj.DtsObjectType == "pipeline") {
                childobj.EmitPipeline(newindent);
            } else if (childobj.DtsObjectType == "DTS:PrecedenceConstraint") {
                // ignore it - it's already been handled
            } else if (childobj.DtsObjectType == "DTS:LoggingOptions") {
                // Ignore it - I can't figure out any useful information on this object
            } else if (childobj.DtsObjectType == "DTS:ForEachVariableMapping") {
                // ignore it - handled earlier
            } else if (childobj.DtsObjectType == "DTS:ForEachEnumerator") {
                // ignore it - handled explicitly by the foreachloop

            } else {
                SourceWriter.Help(this, "I don't yet know how to handle " + childobj.DtsObjectType);
            }
        }

        private void EmitForEachVariableMapping(string indent)
        {
            string varname = FixVariableName(this.Properties["VariableName"]);

            // Look up the variable data
            VariableData vd = _var_dict[varname];

            // Produce a line
            SourceWriter.WriteLine(String.Format(@"{0}{1} = ({3})iter.ItemArray[{2}];", indent, varname, this.Properties["ValueIndex"], vd.CSharpType));
        }

        private void EmitForEachLoop(string indent)
        {
            // Retrieve the three settings from the for loop
            string iterator = FixVariableName(GetChildByType("DTS:ForEachEnumerator").GetChildByType("DTS:ObjectData").Children[0].Attributes["VarName"]);

            // Write it out - I'm assuming this is a data table for now
            SourceWriter.WriteLine(String.Format(@"{0}int current_row_num = 0;", indent));
            SourceWriter.WriteLine(String.Format(@"{0}foreach (DataRow iter in {1}.Rows) {{", indent, iterator));
            SourceWriter.WriteLine(String.Format(@"{0}    Console.WriteLine(""{{0}} Loop: On row {{1}} of {{2}}"", DateTime.Now, ++current_row_num, {1}.Rows.Count);", indent, iterator));
            SourceWriter.WriteLine();
            string newindent = indent + "    ";
            SourceWriter.WriteLine(String.Format(@"{0}// Setup all variable mappings", newindent, iterator));

            // Do all the iteration mappings first
            foreach (SsisObject childobj in Children) {
                if (childobj.DtsObjectType == "DTS:ForEachVariableMapping") {
                    childobj.EmitForEachVariableMapping(indent + "    ");
                }
            }
            SourceWriter.WriteLine();

            // Other interior objects and tasks
            EmitChildObjects(indent);

            // Close the loop
            SourceWriter.WriteLine(String.Format(@"{0}}}", indent));
        }

        private void EmitForLoop(string indent)
        {
            // Retrieve the three settings from the for loop
            string init = System.Net.WebUtility.HtmlDecode(this.Properties["InitExpression"]).Replace("@","");
            string eval = System.Net.WebUtility.HtmlDecode(this.Properties["EvalExpression"]).Replace("@","");
            string assign = System.Net.WebUtility.HtmlDecode(this.Properties["AssignExpression"]).Replace("@","");

            // Write it out
            SourceWriter.WriteLine(String.Format(@"{0}for ({1};{2};{3}) {{", indent, init, eval, assign));

            // Inner stuff ?
            EmitChildObjects(indent);

            // Close the loop
            SourceWriter.WriteLine(String.Format(@"{0}}}", indent));
        }

        /// <summary>
        /// Write out a function call
        /// </summary>
        /// <param name="indent"></param>
        /// <param name="sw"></param>
        private void EmitFunctionCall(string indent, string scope_variables)
        {
            // Is this call disabled?
            if (Properties["Disabled"] == "-1") {
                SourceWriter.WriteLine(String.Format(@"{0}// SSIS records this function call is disabled", indent));
                SourceWriter.WriteLine(String.Format(@"{0}// {1}({2});", indent, GetFunctionName(), scope_variables));
            } else {
                SourceWriter.WriteLine(String.Format(@"{0}{1}({2});", indent, GetFunctionName(), scope_variables));
            }
        }

        /// <summary>
        /// Write out an SQL statement
        /// </summary>
        /// <param name="indent_depth"></param>
        /// <param name="sw"></param>
        private void EmitSqlStatement(string indent)
        {
            // Retrieve the connection string object
            string conn_guid = Attributes["SQLTask:Connection"];
            string connstr = ConnectionWriter.GetConnectionStringName(conn_guid);
            string connprefix = ConnectionWriter.GetConnectionStringPrefix(conn_guid);
            bool is_sqlcmd = IsSqlCmdStatement(Attributes["SQLTask:SqlStatementSource"]);

            // Report potential problems - can we programmatically convert an OleDb connection into an ADO.NET one?
            string fixup = "";
            if (connprefix == "OleDb") {
                SourceWriter.Help(this, "DESSIST had to rewrite an OleDb connection as an ADO.NET connection.  Please check it for correctness.");
                connprefix = "Sql";
                fixup = @".FixupOleDb()";
            }

            // Retrieve the SQL String and put it in a resource
            string sql_attr_name = ProjectWriter.AddSqlResource(GetParentDtsName(), Attributes["SQLTask:SqlStatementSource"]);

            // Are we going to return anything?  Prepare a variable to hold it
            if (this.Attributes["SQLTask:ResultType"] == "ResultSetType_SingleRow") {
                SourceWriter.WriteLine(@"{0}object result = null;", indent, connstr);
            } else {
                SourceWriter.WriteLine(@"{0}DataTable result = null;", indent, connstr);
            }
            SourceWriter.WriteLine(@"{0}Console.WriteLine(""{{0}} SQL: {1}"", DateTime.Now);", indent, sql_attr_name);
            SourceWriter.WriteLine();

            // Open the connection
            SourceWriter.WriteLine(@"{0}using (var conn = new {2}Connection(ConfigurationManager.AppSettings[""{1}""]{3})) {{", indent, connstr, connprefix, fixup);
            SourceWriter.WriteLine(@"{0}    conn.Open();", indent);

            // Does this SQL statement include any nested "GO" commands?  Let's make a simple call
            string sql_variable_name = null;
            if (is_sqlcmd) {
                SourceWriter.WriteLine();
                SourceWriter.WriteLine(@"{0}    // This SQL statement is a compound statement that must be run from the SQL Management object", indent);
                SourceWriter.WriteLine(@"{0}    ServerConnection svrconn = new ServerConnection(conn);", indent);
                SourceWriter.WriteLine(@"{0}    Server server = new Server(svrconn);", indent);
                SourceWriter.WriteLine(@"{0}    server.ConnectionContext.SqlExecutionModes = SqlExecutionModes.CaptureSql;", indent, sql_attr_name);
                SourceWriter.WriteLine(@"{0}    server.ConnectionContext.ExecuteNonQuery(Resource1.{1});", indent, sql_attr_name);
                //SourceWriter.WriteLine(@"{0}    int statement = 0;", indent);
                SourceWriter.WriteLine(@"{0}    foreach (string s in server.ConnectionContext.CapturedSql.Text) {{", indent);
                //SourceWriter.WriteLine(@"{0}        Console.WriteLine(""{{0}} Statement {{1}} of {{2}}"", DateTime.Now, ++statement, server.ConnectionContext.CapturedSql.Text.Count);", indent);
                sql_variable_name = "s";
                indent = indent + "    ";
            } else {
                sql_variable_name = "Resource1." + sql_attr_name;
            }

            // Write the using clause for the connection
            SourceWriter.WriteLine(@"{0}    using (var cmd = new {2}Command({1}, conn)) {{", indent, sql_variable_name, connprefix);

            // Handle our parameter binding
            foreach (SsisObject childobj in Children) {
                if (childobj.DtsObjectType == "SQLTask:ParameterBinding") {
                    SourceWriter.WriteLine(@"{0}        cmd.Parameters.AddWithValue(""{1}"", {2});", indent, childobj.Attributes["SQLTask:ParameterName"], FixVariableName(childobj.Attributes["SQLTask:DtsVariableName"]));
                }
            }

            // What type of variable reading are we doing?
            if (this.Attributes["SQLTask:ResultType"] == "ResultSetType_SingleRow") {
                SourceWriter.WriteLine(@"{0}        result = cmd.ExecuteScalar();", indent);
            } else {
                SourceWriter.WriteLine(@"{0}        {1}DataReader dr = cmd.ExecuteReader();", indent, connprefix);
                SourceWriter.WriteLine(@"{0}        result = new DataTable();", indent);
                SourceWriter.WriteLine(@"{0}        result.Load(dr);", indent);
                SourceWriter.WriteLine(@"{0}        dr.Close();", indent);
            }

            // Finish up the SQL call
            SourceWriter.WriteLine(@"{0}    }}", indent);
            SourceWriter.WriteLine(@"{0}}}", indent);

            // Do we have a result binding?
            SsisObject binding = GetChildByType("SQLTask:ResultBinding");
            if (binding != null) {
                string varname = binding.Attributes["SQLTask:DtsVariableName"];
                string fixedname = FixVariableName(varname);
                VariableData vd = _var_dict[fixedname];

                // Emit our binding
                SourceWriter.WriteLine(@"{0}", indent);
                SourceWriter.WriteLine(@"{0}// Bind results to {1}", indent, varname);
                if (vd.CSharpType == "DataTable") {
                    SourceWriter.WriteLine(@"{0}{1} = result;", indent, FixVariableName(varname));
                } else {
                    SourceWriter.WriteLine(@"{0}{1} = ({2})result;", indent, FixVariableName(varname), vd.CSharpType);
                }
            }

            // Clean up properly if this was a compound statement
            if (is_sqlcmd) {
                indent = indent.Substring(0, indent.Length - 4);
                SourceWriter.WriteLine(@"{0}}}", indent);
            }
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

        #region Pipeline Logic
        private void EmitPipeline(string indent)
        {
            // Find the component container
            var component_container = GetChildByType("DTS:ObjectData").GetChildByType("pipeline").GetChildByType("components");
            if (component_container == null) {
                SourceWriter.Help(this, "Unable to find SSIS components!");
                return;
            }

            // Produce a "row count" variable we can use
            //SourceWriter.WriteLine(@"{0}int row_count = 0;", indent);

            // Keep track of original components
            List<SsisObject> components = new List<SsisObject>();
            components.AddRange(component_container.Children);

            // Produce all the readers
            foreach (SsisObject child in component_container.GetChildrenByTypeAndAttr("componentClassID", "{BCEFE59B-6819-47F7-A125-63753B33ABB7}")) {
                child.EmitPipelineReader(this, indent);
                components.Remove(child);
            }

            // Iterate through all transformations
            foreach (SsisObject child in component_container.GetChildrenByTypeAndAttr("componentClassID", "{BD06A22E-BC69-4AF7-A69B-C44C2EF684BB}")) {
                child.EmitPipelineTransform(this, indent);
                components.Remove(child);
            }

            // Iterate through all transformations - this is basically the same thing but this time it uses "expression" rather than "type conversion"
            foreach (SsisObject child in component_container.GetChildrenByTypeAndAttr("componentClassID", "{2932025B-AB99-40F6-B5B8-783A73F80E24}")) {
                child.EmitPipelineTransform(this, indent);
                components.Remove(child);
            }

            // Iterate through unions
            foreach (SsisObject child in component_container.GetChildrenByTypeAndAttr("componentClassID", "{4D9F9B7C-84D9-4335-ADB0-2542A7E35422}")) {
                child.EmitPipelineUnion(this, indent);
                components.Remove(child);
            }

            // Iterate through all multicasts
            foreach (SsisObject child in component_container.GetChildrenByTypeAndAttr("componentClassID", "{1ACA4459-ACE0-496F-814A-8611F9C27E23}")) {
                //child.EmitPipelineMulticast(this, indent);
                SourceWriter.WriteLine(@"{0}// MULTICAST: Using all input and writing to multiple outputs", indent);
                components.Remove(child);
            }

            // Process all the writers
            foreach (SsisObject child in component_container.GetChildrenByTypeAndAttr("componentClassID", "{5A0B62E8-D91D-49F5-94A5-7BE58DE508F0}")) {
                child.EmitPipelineWriter(this, indent);
                components.Remove(child);
            }

            // Report all unknown components
            foreach (SsisObject component in components) {
                SourceWriter.Help(this, "I don't know how to process componentClassID = " + component.Attributes["componentClassID"]);
            }
        }

        private List<SsisObject> GetChildrenByTypeAndAttr(string attr_key, string value)
        {
            List<SsisObject> list = new List<SsisObject>();
            foreach (SsisObject child in Children) {
                string attr = null;
                if (child.Attributes.TryGetValue(attr_key, out attr) && string.Equals(attr, value)) {
                    list.Add(child);
                }
            }
            return list;
        }

        private void EmitPipelineUnion(SsisObject pipeline, string indent)
        {
            SourceWriter.WriteLine();
            SourceWriter.WriteLine(@"{0}// {1}", indent, Attributes["name"]);

            // Create a new datatable
            SourceWriter.WriteLine(@"{0}DataTable component{1} = new DataTable();", indent, this.Attributes["id"]);

            // Add the columns we're generating
            int i = 0;
            List<SsisObject> transforms = this.GetChildByType("outputs").GetChildByTypeAndAttr("output", "isErrorOut", "false").GetChildByType("outputColumns").Children;
            List<string> colnames = new List<string>();
            foreach (SsisObject outcol in transforms) {
                LineageObject lo = new LineageObject(outcol.Attributes["lineageId"], "component" + this.Attributes["id"], outcol.Attributes["name"]);
                i++;
                pipeline._lineage_columns.Add(lo);

                // Print out this column
                SourceWriter.WriteLine(@"{0}component{1}.Columns.Add(new DataColumn(""{2}"", typeof({3})));", indent, this.Attributes["id"], outcol.Attributes["name"], LookupSsisTypeName(outcol.Attributes["dataType"]));
                DataTable dt = new DataTable();
                colnames.Add(outcol.Attributes["name"]);
            }

            // Loop through all the inputs and process them!
            SsisObject outputcolumns = this.GetChildByType("outputs").GetChildByType("output").GetChildByType("outputColumns");
            foreach (SsisObject inputtable in this.GetChildByType("inputs").Children) {

                // Find the name of the table by looking at the first column
                SsisObject input_columns = inputtable.GetChildByType("inputColumns");
                SsisObject metadata = inputtable.GetChildByType("externalMetadataColumns");
                if (input_columns != null) {
                    SsisObject first_col = input_columns.Children[0];
                    LineageObject first_input = pipeline.GetLineageObjectById(first_col.Attributes["lineageId"]);
                    string component = first_input.DataTableName;

                    // Read through all rows in the table
                    SourceWriter.WriteLine(@"{0}for (int row = 0; row < {1}.Rows.Count; row++) {{", indent, component);
                    SourceWriter.WriteLine(@"{0}    DataRow dr = component{1}.NewRow();", indent, this.Attributes["id"]);

                    // Loop through all the columns and insert them
                    foreach (SsisObject col in inputtable.GetChildByType("inputColumns").Children) {
                        LineageObject l = pipeline.GetLineageObjectById(col.Attributes["lineageId"]);

                        // find the matching external metadata column 
                        string outcolname = "";
                        SsisObject mdcol = metadata.GetChildByTypeAndAttr("externalMetadataColumn", "id", col.Attributes["externalMetadataColumnId"]);
                        if (mdcol == null) {
                            SsisObject prop = col.GetChildByType("properties").GetChildByType("property");
                            SsisObject outcol = outputcolumns.GetChildByTypeAndAttr("outputColumn", "id", prop.ContentValue);
                            outcolname = outcol.Attributes["name"];
                        } else {
                            outcolname = mdcol.Attributes["name"];
                        }

                        // Write out the expression
                        SourceWriter.WriteLine(@"{0}    dr[""{1}""] = {2};", indent, outcolname, l.ToString());
                    }

                    // Write the end of this code block
                    SourceWriter.WriteLine(@"{0}    component{1}.Rows.Add(dr);", indent, this.Attributes["id"]);
                    SourceWriter.WriteLine(@"{0}}}", indent);
                }
            }
        }

        private void EmitPipelineWriter(SsisObject pipeline, string indent)
        {
            SourceWriter.WriteLine();
            SourceWriter.WriteLine(@"{0}// {1}", indent, Attributes["name"]);

            // Get the connection string GUID: it's this.connections.connection
            string conn_guid = this.GetChildByType("connections").GetChildByType("connection").Attributes["connectionManagerID"];
            string connstr = ConnectionWriter.GetConnectionStringName(conn_guid);
            string connprefix = ConnectionWriter.GetConnectionStringPrefix(conn_guid);

            // Report potential problems - can we programmatically convert an OleDb connection into an ADO.NET one?
            string fixup = "";
            if (connprefix == "OleDb") {
                SourceWriter.Help(this, "DESSIST had to rewrite an OleDb connection as an ADO.NET connection.  Please check it for correctness.");
                connprefix = "Sql";
                fixup = @".Replace(""Provider=SQLNCLI10.1;"","""")";
            }

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
                LineageObject lo = pipeline.GetLineageObjectById(lineageId);

                // Parameter setup instructions
                if (lo == null) {
                    SourceWriter.Help(this, "I couldn't find lineage column " + lineageId);
                    paramsetup.AppendFormat(@"{0}            // Unable to find column {1}{2}", indent, lineageId, Environment.NewLine);
                } else {

                    // Is this a string?  If so, forcibly truncate it
                    if (mdcol.Attributes["dataType"] == "str") {
                        paramsetup.AppendFormat(@"{0}            cmd.Parameters.Add(new SqlParameter(""@{1}"", SqlDbType.VarChar, {3}, ParameterDirection.Input, false, 0, 0, null, DataRowVersion.Current, {2}));
", indent, mdcol.Attributes["name"], lo.ToString(), mdcol.Attributes["length"]);
                    } else if (mdcol.Attributes["dataType"] == "wstr") {
                        paramsetup.AppendFormat(@"{0}            cmd.Parameters.Add(new SqlParameter(""@{1}"", SqlDbType.NVarChar, {3}, ParameterDirection.Input, false, 0, 0, null, DataRowVersion.Current, {2}));
", indent, mdcol.Attributes["name"], lo.ToString(), mdcol.Attributes["length"]);
                    } else {
                        paramsetup.AppendFormat(@"{0}            cmd.Parameters.AddWithValue(""@{1}"",{2});
", indent, mdcol.Attributes["name"], lo.ToString());
                    }
                }
            }
            colnames.Length -= 2;
            varnames.Length -= 2;

            // Produce the SQL statement
            sql.Append("INSERT INTO ");
            sql.Append(GetChildByType("properties").GetChildByTypeAndAttr("property", "name", "OpenRowset").ContentValue);
            sql.Append(" (");
            sql.Append(colnames.ToString());
            sql.Append(") VALUES (");
            sql.Append(varnames.ToString());
            sql.Append(")");
            string sql_resource_name = ProjectWriter.AddSqlResource(GetParentDtsName() + "_WritePipe", sql.ToString());

            // Produce a data set that we're going to process - name it after ourselves
            SourceWriter.WriteLine(@"{0}DataTable component{1} = new DataTable();", indent, this.Attributes["id"]);

            // Write the using clause for the connection
            SourceWriter.WriteLine(@"{0}using (var conn = new {2}Connection(ConfigurationManager.AppSettings[""{1}""]{3})) {{", indent, connstr, connprefix, fixup);
            SourceWriter.WriteLine(@"{0}    conn.Open();", indent);

            // TODO: SQL Parameters should go in here

            // Check the inputs to see what component we're using as the source
            string component = null;
            foreach (SsisObject incol in this.GetChildByType("inputs").GetChildByType("input").GetChildByType("inputColumns").Children) {
                LineageObject input = pipeline.GetLineageObjectById(incol.Attributes["lineageId"]);
                if (component == null) {
                    component = input.DataTableName;
                } else {
                    if (component != input.DataTableName) {
                        //SourceWriter.Help(this, "This SSIS pipeline is merging different component tables!");
                        // From closer review, this doesn't look like a problem - it's most likely due to transformations occuring on output of a table
                    }
                }
            }

            // This is the laziest possible way to do this insert - may want to improve it later
            SourceWriter.WriteLine(@"{0}    for (int row = 0; row < {1}.Rows.Count; row++) {{", indent, component);
            SourceWriter.WriteLine(@"{0}        using (var cmd = new {2}Command(Resource1.{1}, conn)) {{", indent, sql_resource_name, connprefix);
            SourceWriter.WriteLine(paramsetup.ToString());
            SourceWriter.WriteLine(@"{0}            cmd.ExecuteNonQuery();", indent);
            SourceWriter.WriteLine(@"{0}        }}", indent);
            SourceWriter.WriteLine(@"{0}    }}", indent);
            SourceWriter.WriteLine(@"{0}}}", indent);
        }

        private void EmitPipelineTransform(SsisObject pipeline, string indent)
        {
            //SourceWriter.WriteLine();
            //SourceWriter.WriteLine(@"{0}// {1}", indent, Attributes["name"]);

            // Create a new datatable
            //SourceWriter.WriteLine(@"{0}DataTable component{1} = new DataTable();", indent, this.Attributes["id"]);

            // Add the columns we're generating
            List<SsisObject> transforms = this.GetChildByType("outputs").GetChildByTypeAndAttr("output", "isErrorOut", "false").GetChildByType("outputColumns").Children;
            //foreach (SsisObject outcol in transforms) {
            //    LineageObject lo = new LineageObject(outcol.Attributes["lineageId"], "component" + this.Attributes["id"], outcol.Attributes["name"]);
            //    pipeline._lineage_columns.Add(lo);

            //    // Print out this column
            //    //SourceWriter.WriteLine(@"{0}component{1}.Columns.Add(new DataColumn(""{2}"", typeof({3})));", indent, this.Attributes["id"], outcol.Attributes["name"], LookupSsisTypeName(outcol.Attributes["dataType"]));
            //    DataTable dt = new DataTable();
            //}

            // Check the inputs to see what component we're using as the source
            string component = "component1";
            SsisObject inputcolumns = this.GetChildByType("inputs").GetChildByType("input").GetChildByType("inputColumns");
            if (inputcolumns != null) {
                foreach (SsisObject incol in inputcolumns.Children) {
                    LineageObject input = pipeline.GetLineageObjectById(incol.Attributes["lineageId"]);
                    if (component == null) {
                        component = input.DataTableName;
                    } else {
                        if (component != input.DataTableName) {
                            SourceWriter.Help(this, "This SSIS pipeline is merging different component tables!");
                        }
                    }
                }
            }

            // Populate these columns
            //SourceWriter.WriteLine(@"{0}for (int row = 0; row < {1}.Rows.Count; row++) {{", indent, component);
            //SourceWriter.WriteLine(@"{0}    DataRow dr = component{1}.NewRow();", indent, this.Attributes["id"]);

            // Let's see if we can generate some code to do these conversions!
            foreach (SsisObject outcol in transforms) {
                LineageObject source_lineage = null;
                string expression = null;

                // Find property "expression"
                if (outcol.Children.Count > 0) {
                    foreach (SsisObject property in outcol.GetChildByType("properties").Children) {
                        if (property.Attributes["name"] == "SourceInputColumnLineageID") {
                            source_lineage = pipeline.GetLineageObjectById(property.ContentValue);
                            expression = String.Format(@"Convert.ChangeType({1}.Rows[row][""{2}""], typeof({0}));", LookupSsisTypeName(outcol.Attributes["dataType"]), source_lineage.DataTableName, source_lineage.FieldName);
                        } else if (property.Attributes["name"] == "FastParse") {
                            // Don't need to do anything here
                        } else if (property.Attributes["name"] == "Expression") {

                            // Is this a lineage column?
                            expression = FixExpression(LookupSsisTypeName(outcol.Attributes["dataType"]), pipeline._lineage_columns, property.ContentValue, true);
                        } else if (property.Attributes["name"] == "FriendlyExpression") {
                            // This comment is useless - SourceWriter.WriteLine(@"{0}    // {1}", indent, property.ContentValue);
                        } else {
                            SourceWriter.Help(this, "I don't understand the output column property '" + property.Attributes["name"] + "'");
                        }
                    }

                    // If we haven't been given an explicit expression, just use this
                    if (String.IsNullOrEmpty(expression)) {
                        SourceWriter.Help(this, "I'm trying to do a transform, but I haven't found an expression to use.");
                    } else {

                        // Put this transformation back into the lineage table for later use!
                        LineageObject lo = new LineageObject(outcol.Attributes["lineageId"], expression);
                        pipeline._lineage_columns.Add(lo);
                    }
                } else {
                    SourceWriter.Help(this, "I'm trying to do a transform, but I don't have any properties to use.");
                }
            }

            // Write the end of this code block
            //SourceWriter.WriteLine(@"{0}    component{1}.Rows.Add(dr);", indent, this.Attributes["id"]);
            //SourceWriter.WriteLine(@"{0}}}", indent);
        }

        private void EmitPipelineReader(SsisObject pipeline, string indent)
        {
            SourceWriter.WriteLine();
            SourceWriter.WriteLine(@"{0}// {1}", indent, Attributes["name"]);

            // Get the connection string GUID: it's this.connections.connection
            string conn_guid = this.GetChildByType("connections").GetChildByType("connection").Attributes["connectionManagerID"];
            string connstr = ConnectionWriter.GetConnectionStringName(conn_guid);
            string connprefix = ConnectionWriter.GetConnectionStringPrefix(conn_guid);

            // Get the SQL statement
            string sql = this.GetChildByType("properties").GetChildByTypeAndAttr("property", "name", "SqlCommand").ContentValue;
            if (sql == null) {
                string rowset = this.GetChildByType("properties").GetChildByTypeAndAttr("property", "name", "OpenRowset").ContentValue;
                if (rowset == null) {
                    sql = "COULD NOT FIND SQL STATEMENT";
                    SourceWriter.Help(pipeline, String.Format("Could not find SQL for {0} in {1}", Attributes["name"], this.DtsId));
                } else {
                    sql = "SELECT * FROM " + rowset;
                }
            }
            string sql_resource_name = ProjectWriter.AddSqlResource(GetParentDtsName() + "_ReadPipe", sql);

            // Produce a data set that we're going to process - name it after ourselves
            SourceWriter.WriteLine(@"{0}DataTable component{1} = new DataTable();", indent, this.Attributes["id"]);

            // Keep track of the lineage of all of our output columns 
            // TODO: Handle error output columns
            int i = 0;
            foreach (SsisObject outcol in this.GetChildByType("outputs").GetChildByTypeAndAttr("output", "isErrorOut", "false").GetChildByType("outputColumns").Children) {
                LineageObject lo = new LineageObject(outcol.Attributes["lineageId"], "component" + this.Attributes["id"], outcol.Attributes["name"]);
                i++;
                pipeline._lineage_columns.Add(lo);
            }

            // Write the using clause for the connection
            SourceWriter.WriteLine(@"{0}using (var conn = new {2}Connection(ConfigurationManager.AppSettings[""{1}""])) {{", indent, connstr, connprefix);
            SourceWriter.WriteLine(@"{0}    conn.Open();", indent);
            SourceWriter.WriteLine(@"{0}    using (var cmd = new {2}Command(Resource1.{1}, conn)) {{", indent, sql_resource_name, connprefix);

            // Okay, let's load the parameters
            var paramlist = this.GetChildByType("properties").GetChildByTypeAndAttr("property", "name", "ParameterMapping");
            if (paramlist != null && paramlist.ContentValue != null) {
                string[] p = paramlist.ContentValue.Split(';');
                int paramnum = 0;
                foreach (string oneparam in p) {
                    if (!String.IsNullOrEmpty(oneparam)) {
                        string[] parts = oneparam.Split(',');
                        Guid g = Guid.Parse(parts[1]);

                        // Look up this GUID - can we find it?
                        SsisObject v = GetObjectByGuid(g);
                        if (connprefix == "OleDb") {
                            SourceWriter.WriteLine(@"{0}        cmd.Parameters.Add(new OleDbParameter(""@p{2}"",{1}));", indent, v.DtsObjectName, paramnum);
                        } else {
                            SourceWriter.WriteLine(@"{0}        cmd.Parameters.AddWithValue(""@{1}"",{2});", indent, parts[0], v.DtsObjectName);
                        }
                    }
                    paramnum++;
                }
            }

            // Finish up the pipeline reader
            SourceWriter.WriteLine(@"{0}        {1}DataReader dr = cmd.ExecuteReader();", indent, connprefix);
            SourceWriter.WriteLine(@"{0}        component{1}.Load(dr);", indent, this.Attributes["id"]);
            SourceWriter.WriteLine(@"{0}        dr.Close();", indent);
            SourceWriter.WriteLine(@"{0}    }}", indent);
            SourceWriter.WriteLine(@"{0}}}", indent);
        }
        #endregion

        #region Helper functions

        /// <summary>
        /// Determines if the SQL statement is written in SQLCMD style (e.g. including "GO" statements) or as a regular SQL string
        /// </summary>
        /// <param name="sql"></param>
        /// <returns></returns>
        private static bool IsSqlCmdStatement(string sql)
        {
            // TODO: There really should be a better way to determine this
            return sql.Contains("\nGO");
        }

        public static string FixExpression(string expected_type, List<LineageObject> list, string expression, bool inline)
        {
            ExpressionData ed = new ExpressionData(expected_type, list, expression);
            return ed.ToCSharp(inline);
        }

        /// <summary>
        /// Converts the namespace into something usable by C#
        /// </summary>
        /// <param name="original_variable_name"></param>
        /// <returns></returns>
        public static string FixVariableName(string original_variable_name)
        {
            // We are simply stripping out namespaces for the moment
            int p = original_variable_name.IndexOf("::");
            if (p > 0) {
                return original_variable_name.Substring(p + 2);
            }
            return original_variable_name;
        }

        public string GetScopeVariables(bool include_type)
        {
            // Do we have any variables to pass?
            StringBuilder p = new StringBuilder();
            if (include_type) {
                foreach (VariableData vd in _scope_variables) {
                    p.AppendFormat("ref {0} {1}, ", vd.CSharpType, vd.VariableName);
                }
            } else {
                foreach (VariableData vd in _scope_variables) {
                    p.AppendFormat("ref {0}, ", vd.VariableName);
                }
            }
            if (p.Length > 0) p.Length -= 2;
            return p.ToString();
        }

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

        public static string LookupSsisTypeName(string p)
        {
            // Skip Data Transformation Underscore
            if (p.StartsWith("DT_")) p = p.Substring(3);
            p = p.ToLower();

            // Okay, let's check real stuff
            if (p == "i2") {
                return "System.Int16";
            } else if (p == "i4") {
                return "System.Int32";
            } else if (p == "i8") {
                return "System.Int64";
            } else if (p == "str" || p == "wstr") {
                return "System.String";
            } else if (p == "dbtimestamp") {
                return "System.DateTime";
            } else if (p == "r4" || p == "r8") {
                return "double";

            // Currency
            } else if (p == "cy" || p == "numeric") { 
                return "System.Decimal";
            } else {
                SourceWriter.Help(null, "I don't yet understand the SSIS type named " + p);
            }
            return null;
        }

        private static Dictionary<Guid, SsisObject> _guid_lookup = new Dictionary<Guid, SsisObject>();
        public static SsisObject GetObjectByGuid(string s)
        {
            return GetObjectByGuid(Guid.Parse(s));
        }

        public static SsisObject GetObjectByGuid(Guid g)
        {
            var v = _guid_lookup[g];
            if (v == null) {
                SourceWriter.Help(null, "Can't find object matching GUID " + g.ToString());
            }
            return v;
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

        public Guid GetNearestGuid()
        {
            SsisObject o = this;
            while (o != null && (o.DtsId == null || o.DtsId == Guid.Empty)) {
                o = o.Parent;
            }
            if (o != null) return o.DtsId;
            return Guid.Empty;
        }
        #endregion
    }
}
