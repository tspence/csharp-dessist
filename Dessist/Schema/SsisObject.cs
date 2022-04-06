/*
 * 2012-2015 Ted Spence, http://tedspence.com
 * License: http://www.apache.org/licenses/LICENSE-2.0 
 * Home page: https://github.com/tspence/csharp-dessist
 */

using System.Text;
using System.Xml;

// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo

namespace Dessist
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

        /// <summary>
        /// A user-readable explanation of what this is
        /// </summary>
        public string Description;

        /// <summary>
        /// The GUID for this object
        /// </summary>
        private Guid _dtsId;

        /// <summary>
        /// Attributes, if any
        /// </summary>
        public readonly Dictionary<string, string> Attributes = new Dictionary<string, string>();

        /// <summary>
        /// All the properties defined in the SSIS
        /// </summary>
        public readonly Dictionary<string, string> Properties = new Dictionary<string, string>();

        /// <summary>
        /// List of child elements in SSIS
        /// </summary>
        public readonly List<SsisObject> Children = new List<SsisObject>();

        public SsisObject? Parent;
        public string ContentValue;

        private readonly SsisProject _project;
        private string? _functionName;
        private string? _folderName;
        private readonly List<ProgramVariable> _scopeVariables = new List<ProgramVariable>();
        private readonly List<LineageObject> _lineageColumns = new List<LineageObject>();

        /// <summary>
        /// Construct a new SSIS object within a project
        /// </summary>
        /// <param name="project"></param>
        /// <param name="parent"></param>
        public SsisObject(SsisProject project, SsisObject? parent)
        {
            _project = project;
            Parent = parent;
            _dtsId = Guid.Empty;
        }

        /// <summary>
        /// Shortcut to find a lineage object
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private LineageObject? GetLineageObjectById(string id)
        {
            return (from LineageObject l in _lineageColumns where (l.LineageId == id) select l).FirstOrDefault();
        }

        /// <summary>
        /// Set a property
        /// </summary>
        /// <param name="prop_name"></param>
        /// <param name="prop_value"></param>
        private void SetProperty(string prop_name, string prop_value)
        {
            switch (prop_name)
            {
                case "ObjectName":
                    DtsObjectName = prop_value;
                    break;
                case "DTSID":
                    _dtsId = Guid.Parse(prop_value);
                    _project.RegisterObject(_dtsId, this);
                    break;
                case "Description":
                    Description = prop_value;
                    break;
                default:
                    Properties[prop_name] = prop_value;
                    break;
            }
        }

        /// <summary>
        /// Retrieve a child with the specific name
        /// </summary>
        /// <param name="objectname"></param>
        public SsisObject? GetChildByType(string objectname)
        {
            return (from SsisObject o in Children where o.DtsObjectType == objectname select o).FirstOrDefault();
        }

        /// <summary>
        /// Retrieve a child with the specific name
        /// </summary>
        /// <param name="objectname"></param>
        /// <param name="attribute"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public SsisObject? GetChildByTypeAndAttr(string objectname, string attribute, string value)
        {
            return (from SsisObject o in Children
                where (o.DtsObjectType == objectname)
                      && (o.Attributes[attribute] == value)
                select o).FirstOrDefault();
        }

        /// <summary>
        /// Produce this variable to the current stream
        /// </summary>
        /// <param name="indent"></param>
        /// <param name="as_global"></param>
        /// <returns></returns>
        internal ProgramVariable EmitVariable(string indent, bool as_global)
        {
            var vd = new ProgramVariable(this, as_global);

            // Do we add comments for these variables?
            var privilege = "";
            if (as_global)
            {
                if (!string.IsNullOrEmpty(vd.Comment))
                {
                    SourceWriter.WriteLine();
                    SourceWriter.WriteLine("{0}/// <summary>", indent);
                    SourceWriter.WriteLine("{0}/// {1}", indent, vd.Comment);
                    SourceWriter.WriteLine("{0}/// </summary>", indent);
                }

                privilege = "public static ";
            }

            // Write it out
            if (string.IsNullOrEmpty(vd.DefaultValue))
            {
                SourceWriter.WriteLine($"{indent}{privilege}{vd.CSharpType} {vd.VariableName};");
            }
            else
            {
                SourceWriter.WriteLine($"{indent}{privilege}{vd.CSharpType} {vd.VariableName} = {vd.DefaultValue};");
            }

            // Keep track of variables so we can do type conversions in the future!
            _project.RegisterVariable(vd.VariableName, vd);
            return vd;
        }

        /// <summary>
        /// Produce this variable to the current stream
        /// </summary>
        /// <param name="sqlMode"></param>
        /// <param name="indent"></param>
        /// <param name="scope_variables"></param>
        internal void EmitFunction(SqlCompatibilityType sqlMode, string indent, List<ProgramVariable> scope_variables)
        {
            _scopeVariables.AddRange(scope_variables);

            // Header and comments
            SourceWriter.WriteLine();
            if (!string.IsNullOrEmpty(Description))
            {
                SourceWriter.WriteLine("{0}/// <summary>", indent);
                SourceWriter.WriteLine("{0}/// {1}", indent, Description);
                SourceWriter.WriteLine("{0}/// </summary>", indent);
            }

            // Function intro
            SourceWriter.WriteLine($"{indent}public static void {GetFunctionName()}({GetScopeVariables(true)})");
            SourceWriter.WriteLine($"{indent}{{");
            SourceWriter.WriteLine($"{indent}    timer.Enter(\"{GetFunctionName()}\");");

            // What type of executable are we?  Let's check if special handling is required
            var exec_type = Attributes["DTS:ExecutableType"];

            // Child script project - Emit it as a sub-project within the greater solution!
            if (exec_type.StartsWith("Microsoft.SqlServer.Dts.Tasks.ScriptTask.ScriptTask"))
            {
                ProjectWriter.EmitScriptProject(this, indent + "    ");

                // Basic SQL command
            }
            else if (exec_type.StartsWith("Microsoft.SqlServer.Dts.Tasks.ExecuteSQLTask.ExecuteSQLTask"))
            {
                EmitSqlTask(sqlMode, indent);

                // Basic "SEQUENCE" construct - just execute things in order!
            }
            else if (exec_type.StartsWith("STOCK:SEQUENCE"))
            {
                EmitChildObjects(sqlMode, indent);

                // Handle "FOR" and "FOREACH" loop types
            }
            else if (exec_type == "STOCK:FORLOOP")
            {
                EmitForLoop(sqlMode, indent + "    ");
            }
            else if (exec_type == "STOCK:FOREACHLOOP")
            {
                EmitForEachLoop(sqlMode, indent + "    ");
            }
            else if (exec_type == "SSIS.Pipeline.2")
            {
                EmitPipeline(sqlMode, indent + "    ");
            }
            else if (exec_type.StartsWith("Microsoft.SqlServer.Dts.Tasks.SendMailTask.SendMailTask"))
            {
                EmitSendMailTask(indent + "    ");

                // Something I don't yet understand
            }
            else
            {
                SourceWriter.Help(this, "I don't yet know how to handle " + exec_type);
            }

            // TODO: Is there an exception handler?  How many types of event handlers are there?

            // End of function
            SourceWriter.WriteLine("{0}    timer.Leave();", indent);
            SourceWriter.WriteLine("{0}}}", indent);

            // Now emit any other functions that are chained into this
            foreach (var o in Children)
            {
                if (o.DtsObjectType == "DTS:Executable")
                {
                    o.EmitFunction(sqlMode, indent, _scopeVariables);
                }
            }
        }

        private void EmitSendMailTask(string indent)
        {
            // Navigate to our object data
            var mail = GetChildByType("DTS:ObjectData")?.GetChildByType("SendMailTask:SendMailTaskData");

            SourceWriter.WriteLine($"{indent}MailMessage message = new MailMessage();");
            SourceWriter.WriteLine($"{indent}message.To.Add(\"{mail?.Attributes["SendMailTask:To"]}\");");
            SourceWriter.WriteLine($"{indent}message.Subject = \"{mail?.Attributes["SendMailTask:Subject"]}\";");
            SourceWriter.WriteLine($"{indent}message.From = new MailAddress(\"{mail?.Attributes["SendMailTask:From"]}\");");

            // Handle CC/BCC if available
            if (mail.Attributes.TryGetValue("SendMailTask:CC", out var addr) && !string.IsNullOrEmpty(addr))
            {
                SourceWriter.WriteLine($"{indent}message.CC.Add(\"{addr}\");");
            }

            if (mail.Attributes.TryGetValue("SendMailTask:BCC", out addr) && !string.IsNullOrEmpty(addr))
            {
                SourceWriter.WriteLine($"{indent}message.Bcc.Add(\"{1}\");", indent, addr);
            }

            // Process the message source
            var sourcetype = mail.Attributes["SendMailTask:MessageSourceType"];
            if (sourcetype == "Variable")
            {
                SourceWriter.WriteLine($"{indent}message.Body = {mail.Attributes["SendMailTask:MessageSource"].FixVariableName()};");
            }
            else if (sourcetype == "DirectInput")
            {
                SourceWriter.WriteLine($"{indent}message.Body = @\"{mail.Attributes["SendMailTask:MessageSource"].EscapeDoubleQuotes()}\";");
            }
            else
            {
                _project.Log("I don't understand the SendMail message source type '{sourcetype}'");
            }

            // Get the SMTP configuration name
            SourceWriter.WriteLine(
                $"{indent}using (var smtp = new SmtpClient(ConfigurationManager.AppSettings[\"{_project.GetObjectByGuid(Guid.Parse(mail.Attributes["SendMailTask:SMTPServer"])).DtsObjectName}\"])) {{");
            SourceWriter.WriteLine($"{indent}    smtp.Send(message);");
            SourceWriter.WriteLine($"{indent}}}");
        }

        private void EmitSqlTask(SqlCompatibilityType sqlMode, string indent)
        {
            EmitChildObjects(sqlMode, indent);
        }

        private void EmitChildObjects(SqlCompatibilityType sqlMode, string indent)
        {
            var newindent = indent + "    ";

            // To handle precedence data correctly, first make a list of encumbered children
            var modified_children = new List<SsisObject>();
            modified_children.AddRange(Children);

            // Write comments for the precedence data - we'll eventually have to handle this
            var precedence = new List<PrecedenceData>();
            foreach (var o in Children)
            {
                if (o.DtsObjectType == "DTS:PrecedenceConstraint")
                {
                    var pd = new PrecedenceData(_project, o);

                    // Does this precedence data affect any children?  Find it and move it
                    var c = (from obj in modified_children where obj._dtsId == pd.AfterGuid select obj)
                        .FirstOrDefault();
                    modified_children.Remove(c);

                    // Add it to the list
                    precedence.Add(pd);
                }
            }

            if (modified_children.Count > 0)
            {
                SourceWriter.WriteLine("{0}// These calls have no dependencies", newindent);

                // Function body
                foreach (var o in modified_children)
                {
                    // Are there any precedence triggers after this child?
                    PrecedenceChain(sqlMode, o, precedence, newindent);
                }
            }
        }

        private void PrecedenceChain(SqlCompatibilityType sqlMode, SsisObject prior_obj,
            List<PrecedenceData> precedence, string indent)
        {
            EmitOneChild(sqlMode, prior_obj, indent);

            // We just executed "prior_obj" - find what objects it causes to be triggered
            var triggered = (from PrecedenceData pd in precedence where pd.BeforeGuid == prior_obj._dtsId select pd);

            // Iterate through each of these
            foreach (var pd in triggered)
            {
                // Write a comment
                SourceWriter.WriteLine();
                SourceWriter.WriteLine("{0}// {1}", indent, pd.ToString());

                // Is there an expression?
                if (!string.IsNullOrEmpty(pd.Expression))
                {
                    SourceWriter.WriteLine($"{indent}if ({1}) {{", indent,
                        FixExpression("System.Boolean", _lineageColumns, pd.Expression, true));
                    PrecedenceChain(sqlMode, pd.Target, precedence, indent + "    ");
                    SourceWriter.WriteLine($"{indent}}}", indent);
                }
                else
                {
                    PrecedenceChain(sqlMode, pd.Target, precedence, indent);
                }
            }
        }

        private void EmitOneChild(SqlCompatibilityType sqlMode, SsisObject childobj, string newindent)
        {
            // Is this a dummy "Object Data" thing?  If so ignore it and delve deeper
            if (childobj.DtsObjectType == "DTS:ObjectData")
            {
                childobj = childobj.Children[0];
            }

            // For variables, emit them within this function
            if (childobj.DtsObjectType == "DTS:Variable")
            {
                _scopeVariables.Add(childobj.EmitVariable(newindent, false));
            }
            else if (childobj.DtsObjectType == "DTS:Executable")
            {
                childobj.EmitFunctionCall(newindent, GetScopeVariables(false));
            }
            else if (childobj.DtsObjectType == "SQLTask:SqlTaskData")
            {
                childobj.EmitSqlStatement(newindent);

                // TODO: Handle "pipeline" objects
            }
            else if (childobj.DtsObjectType == "pipeline")
            {
                childobj.EmitPipeline(sqlMode, newindent);
            }
            else if (childobj.DtsObjectType == "DTS:PrecedenceConstraint")
            {
                // ignore it - it's already been handled
            }
            else if (childobj.DtsObjectType == "DTS:LoggingOptions")
            {
                // Ignore it - I can't figure out any useful information on this object
            }
            else if (childobj.DtsObjectType == "DTS:ForEachVariableMapping")
            {
                // ignore it - handled earlier
            }
            else if (childobj.DtsObjectType == "DTS:ForEachEnumerator")
            {
                // ignore it - handled explicitly by the for each loop
            }
            else
            {
                SourceWriter.Help(this, "I don't yet know how to handle " + childobj.DtsObjectType);
            }
        }

        private void EmitForEachVariableMapping(string indent)
        {
            var varname = Properties["VariableName"].FixVariableName();

            // Look up the variable data
            var vd = _project.GetVariable(varname);

            // Produce a line
            SourceWriter.WriteLine(
                $@"{indent}{varname} = ({vd?.CSharpType})iter.ItemArray[{Properties["ValueIndex"]}];");
        }

        private void EmitForEachLoop(SqlCompatibilityType sqlMode, string indent)
        {
            // Retrieve the three settings from the for loop
            var iterator = GetChildByType("DTS:ForEachEnumerator")?.GetChildByType("DTS:ObjectData")?.Children[0]
                .Attributes["VarName"].FixVariableName();

            // Write it out - I'm assuming this is a data table for now
            SourceWriter.WriteLine($@"{indent}int current_row_num = 0;");
            SourceWriter.WriteLine($@"{indent}foreach (DataRow iter in {iterator}.Rows) {{");
            SourceWriter.WriteLine(
                $"{indent}    Console.WriteLine(\"{{DateTime.Now}} Loop: On row {{++current_row_num}} of {{iterator}}.Rows.Count);");
            SourceWriter.WriteLine();
            var newindent = indent + "    ";
            SourceWriter.WriteLine($"{newindent}// Setup all variable mappings");

            // Do all the iteration mappings first
            foreach (var childobj in Children.Where(childobj => childobj.DtsObjectType == "DTS:ForEachVariableMapping"))
            {
                childobj.EmitForEachVariableMapping(indent + "    ");
            }

            SourceWriter.WriteLine();

            // Other interior objects and tasks
            EmitChildObjects(sqlMode, indent);

            // Close the loop
            SourceWriter.WriteLine($@"{indent}}}");
        }

        private void EmitForLoop(SqlCompatibilityType sqlMode, string indent)
        {
            // Retrieve the three settings from the for loop
            var init = System.Net.WebUtility.HtmlDecode(Properties["InitExpression"]).Replace("@", "");
            var eval = System.Net.WebUtility.HtmlDecode(Properties["EvalExpression"]).Replace("@", "");
            var assign = System.Net.WebUtility.HtmlDecode(Properties["AssignExpression"]).Replace("@", "");

            // Write it out
            SourceWriter.WriteLine($@"{indent}for ({init};{eval};{assign}) {{");

            // Inner stuff ?
            EmitChildObjects(sqlMode, indent);

            // Close the loop
            SourceWriter.WriteLine($@"{indent}}}");
        }

        /// <summary>
        /// Write out a function call
        /// </summary>
        /// <param name="indent"></param>
        /// <param name="scope_variables"></param>
        private void EmitFunctionCall(string indent, string scope_variables)
        {
            // Is this call disabled?
            if (Properties["Disabled"] == "-1")
            {
                SourceWriter.WriteLine($@"{indent}// SSIS records this function call is disabled");
                SourceWriter.WriteLine($@"{indent}// {GetFunctionName()}({scope_variables});");
            }
            else
            {
                SourceWriter.WriteLine($@"{indent}{GetFunctionName()}({scope_variables});");
            }
        }

        /// <summary>
        /// Write out an SQL statement
        /// </summary>
        /// <param name="indent"></param>
        private void EmitSqlStatement(string indent)
        {
            // Retrieve the connection string object
            var conn_guid = Attributes["SQLTask:Connection"];
            var connstr = ConnectionWriter.GetConnectionStringName(conn_guid);
            var connprefix = ConnectionWriter.GetConnectionStringPrefix(conn_guid);
            var is_sqlcmd = IsSqlCmdStatement(Attributes["SQLTask:SqlStatementSource"]);

            // Report potential problems - can we programmatically convert an OleDb connection into an ADO.NET one?
            var fixup = "";
            if (connprefix == "OleDb")
            {
                SourceWriter.Help(this,
                    "DESSIST had to rewrite an OleDb connection as an ADO.NET connection.  Please check it for correctness.");
                connprefix = "Sql";
                fixup = @".FixupOleDb()";
            }

            // Retrieve the SQL String and put it in a resource
            var raw_sql = Attributes["SQLTask:SqlStatementSource"];

            // Do we need to forcibly convert this code to regular SQL?  This is dangerous, but might be okay if we know it's safe!
            if (is_sqlcmd && !ProjectWriter.UseSqlServerManagementObjects)
            {
                raw_sql = raw_sql.Replace("\nGO", "\n;");
                SourceWriter.Help(this,
                    "Forcibly converted the SQL server script '{0}' (containing multiple statements) to raw SQL (single statement).  Check that this is safe!");
                is_sqlcmd = false;
            }

            var sql_attr_name = ProjectWriter.AddSqlResource(GetParentDtsName(), raw_sql);

            // Do we have a result binding?
            var binding = GetChildByType("SQLTask:ResultBinding");

            // Are we going to return anything?  Prepare a variable to hold it
            if (Attributes.ContainsKey("SQLTask:ResultType") &&
                Attributes["SQLTask:ResultType"] == "ResultSetType_SingleRow")
            {
                SourceWriter.WriteLine($"{indent}object result = null;", indent, connstr);
            }
            else if (binding != null)
            {
                SourceWriter.WriteLine($"{indent}DataTable result = null;", indent, connstr);
            }

            SourceWriter.WriteLine($"{indent}Console.WriteLine(\"{{0}} SQL: {1}\", DateTime.Now);", indent, sql_attr_name);
            SourceWriter.WriteLine();

            // Open the connection
            SourceWriter.WriteLine(
                $"{indent}using (var conn = new {connprefix}Connection(ConfigurationManager.AppSettings[\"{connstr}\"]{fixup})) {{");
            SourceWriter.WriteLine($"{indent}    conn.Open();", indent);

            // Does this SQL statement include any nested "GO" commands?  Let's make a simple call
            string sql_variable_name;
            if (is_sqlcmd)
            {
                SourceWriter.WriteLine();
                SourceWriter.WriteLine(
                    @"{0}    // This SQL statement is a compound statement that must be run from the SQL Management object",
                    indent);
                SourceWriter.WriteLine($"{indent}    ServerConnection svrconn = new ServerConnection(conn);", indent);
                SourceWriter.WriteLine($"{indent}    Server server = new Server(svrconn);", indent);
                SourceWriter.WriteLine(
                    @"{0}    server.ConnectionContext.SqlExecutionModes = SqlExecutionModes.CaptureSql;", indent,
                    sql_attr_name);
                SourceWriter.WriteLine($"{indent}    server.ConnectionContext.ExecuteNonQuery(Resource1.{1});", indent,
                    sql_attr_name);
                SourceWriter.WriteLine($"{indent}    foreach (string s in server.ConnectionContext.CapturedSql.Text) {{",
                    indent);
                sql_variable_name = "s";
                indent = indent + "    ";
            }
            else
            {
                sql_variable_name = "Resource1." + sql_attr_name;
            }

            // Write the using clause for the connection
            SourceWriter.WriteLine($"{indent}    using (var cmd = new {2}Command({1}, conn)) {{", indent, sql_variable_name,
                connprefix);
            if (Attributes.ContainsKey("SQLTask:TimeOut"))
            {
                int timeout = int.Parse(Attributes["SQLTask:TimeOut"]);
                SourceWriter.WriteLine($"{indent}        cmd.CommandTimeout = {1};", indent, timeout);
            }

            // Handle our parameter binding
            foreach (var childobj in Children.Where(childobj => childobj.DtsObjectType == "SQLTask:ParameterBinding"))
            {
                SourceWriter.WriteLine($"{indent}        cmd.Parameters.AddWithValue(\"{1}\", {2});", indent,
                    childobj.Attributes["SQLTask:ParameterName"],
                    childobj.Attributes["SQLTask:DtsVariableName"].FixVariableName());
            }

            // What type of variable reading are we doing?
            if (Attributes.ContainsKey("SQLTask:ResultType") &&
                Attributes["SQLTask:ResultType"] == "ResultSetType_SingleRow")
            {
                SourceWriter.WriteLine($"{indent}        result = cmd.ExecuteScalar();");
            }
            else if (binding != null)
            {
                SourceWriter.WriteLine($"{indent}        {connprefix}DataReader dr = cmd.ExecuteReader();");
                SourceWriter.WriteLine($"{indent}        result = new DataTable();");
                SourceWriter.WriteLine($"{indent}        result.Load(dr);");
                SourceWriter.WriteLine($"{indent}        dr.Close();");
            }
            else
            {
                SourceWriter.WriteLine($"{indent}        cmd.ExecuteNonQuery();");
            }

            // Finish up the SQL call
            SourceWriter.WriteLine($"{indent}    }}", indent);
            SourceWriter.WriteLine($"{indent}}}", indent);

            // Do work with the bound result
            if (binding != null)
            {
                var varname = binding.Attributes["SQLTask:DtsVariableName"];
                var fixedname = varname.FixVariableName();
                var vd = _project.GetVariable(fixedname);

                // Emit our binding
                SourceWriter.WriteLine($"{indent}", indent);
                SourceWriter.WriteLine($"{indent}// Bind results to {varname}");
                if (vd?.CSharpType == "DataTable")
                {
                    SourceWriter.WriteLine($"{indent}{fixedname} = result;");
                }
                else
                {
                    SourceWriter.WriteLine($"{indent}{fixedname} = ({vd?.CSharpType})result;");
                }
            }

            // Clean up properly if this was a compound statement
            if (is_sqlcmd)
            {
                indent = indent.Substring(0, indent.Length - 4);
                SourceWriter.WriteLine($"{indent}}}");
            }
        }

        private string GetParentDtsName()
        {
            var obj = this;
            while (obj is { DtsObjectName: null })
            {
                obj = obj.Parent;
            }

            if (obj == null)
            {
                return "Unnamed";
            }
            else
            {
                return obj.DtsObjectName;
            }
        }

        private void EmitPipeline(SqlCompatibilityType sqlMode, string indent)
        {
            // Find the component container
            var component_container =
                GetChildByType("DTS:ObjectData")?.GetChildByType("pipeline")?.GetChildByType("components");
            if (component_container == null)
            {
                _project.Log("Unable to find DTS:ObjectData/pipeline/components SSIS components!");
                return;
            }

            // Keep track of original components
            var components = new List<SsisObject>();
            components.AddRange(component_container.Children);

            // Produce all the readers
            foreach (var child in component_container.GetChildrenByTypeAndAttr("componentClassID",
                         "{BCEFE59B-6819-47F7-A125-63753B33ABB7}"))
            {
                child.EmitPipelineReader(this, indent);
                components.Remove(child);
            }

            // These are the "flat file source" readers
            foreach (var child in component_container.GetChildrenByTypeAndAttr("componentClassID",
                         "{5ACD952A-F16A-41D8-A681-713640837664}"))
            {
                child.EmitFlatFilePipelineReader(this, indent);
                components.Remove(child);
            }

            // Iterate through all transformations
            foreach (var child in component_container.GetChildrenByTypeAndAttr("componentClassID",
                         "{BD06A22E-BC69-4AF7-A69B-C44C2EF684BB}"))
            {
                child.EmitPipelineTransform(this);
                components.Remove(child);
            }

            // Iterate through all transformations - this is basically the same thing but this time it uses "expression" rather than "type conversion"
            foreach (var child in component_container.GetChildrenByTypeAndAttr("componentClassID",
                         "{2932025B-AB99-40F6-B5B8-783A73F80E24}"))
            {
                child.EmitPipelineTransform(this);
                components.Remove(child);
            }

            // Iterate through unions
            foreach (var child in component_container.GetChildrenByTypeAndAttr("componentClassID",
                         "{4D9F9B7C-84D9-4335-ADB0-2542A7E35422}"))
            {
                child.EmitPipelineUnion(this, indent);
                components.Remove(child);
            }

            // Iterate through all multicasts
            foreach (var child in component_container.GetChildrenByTypeAndAttr("componentClassID",
                         "{1ACA4459-ACE0-496F-814A-8611F9C27E23}"))
            {
                //child.EmitPipelineMulticast(this, indent);
                SourceWriter.WriteLine($"{indent}// MULTICAST: Using all input and writing to multiple outputs", indent);
                components.Remove(child);
            }

            // Process all the writers
            foreach (var child in component_container.GetChildrenByTypeAndAttr("componentClassID",
                         "{5A0B62E8-D91D-49F5-94A5-7BE58DE508F0}"))
            {
                if (sqlMode == SqlCompatibilityType.SQL2008)
                {
                    child.EmitPipelineWriter_TableParam(this, indent);
                }
                else
                {
                    child.EmitPipelineWriter(this, indent);
                }

                components.Remove(child);
            }

            // Report all unknown components
            foreach (SsisObject component in components)
            {
                SourceWriter.Help(this,
                    "I don't know how to process componentClassID = " + component.Attributes["componentClassID"]);
            }
        }

        private List<SsisObject> GetChildrenByTypeAndAttr(string attr_key, string value)
        {
            var list = new List<SsisObject>();
            foreach (var child in Children)
            {
                if (child.Attributes.TryGetValue(attr_key, out var attr) && string.Equals(attr, value))
                {
                    list.Add(child);
                }
            }

            return list;
        }

        private void EmitPipelineUnion(SsisObject pipeline, string indent)
        {
            SourceWriter.WriteLine();
            SourceWriter.WriteLine($"{indent}// {Attributes["name"]}");

            // Create a new datatable
            SourceWriter.WriteLine($"{indent}DataTable component{Attributes["id"]} = new DataTable();");

            // Add the columns we're generating
            var transforms = GetChildByType("outputs")?.GetChildByTypeAndAttr("output", "isErrorOut", "false")?
                .GetChildByType("outputColumns")?.Children;
            foreach (var outcol in transforms ?? new List<SsisObject>())
            {
                var cv = new ColumnVariable(outcol);
                var lo = new LineageObject(cv.LineageID, "component" + Attributes["id"], cv.Name);
                pipeline._lineageColumns.Add(lo);

                // Print out this column
                SourceWriter.WriteLine($"{indent}component{Attributes["id"]}.Columns.Add(new DataColumn(\"{cv.Name}\", typeof({cv.CsharpType()})));");
            }

            // Loop through all the inputs and process them!
            var outputcolumns = GetChildByType("outputs")?.GetChildByType("output")?.GetChildByType("outputColumns");
            foreach (var inputtable in GetChildByType("inputs")?.Children ?? new List<SsisObject>())
            {
                // Find the name of the table by looking at the first column
                var input_columns = inputtable.GetChildByType("inputColumns");
                var metadata = inputtable.GetChildByType("externalMetadataColumns");
                if (input_columns != null)
                {
                    var first_col = input_columns.Children[0];
                    var first_input = pipeline.GetLineageObjectById(first_col.Attributes["lineageId"]);
                    var component = first_input?.DataTableName;

                    // Read through all rows in the table
                    SourceWriter.WriteLine($"{indent}for (int row = 0; row < {component}.Rows.Count; row++) {{");
                    SourceWriter.WriteLine($"{indent}    DataRow dr = component{Attributes["id"]}.NewRow();");

                    // Loop through all the columns and insert them
                    foreach (var col in inputtable.GetChildByType("inputColumns")?.Children ?? new List<SsisObject>())
                    {
                        var l = pipeline.GetLineageObjectById(col.Attributes["lineageId"]);

                        // find the matching external metadata column 
                        string? outcolname;
                        var mdcol = metadata?.GetChildByTypeAndAttr("externalMetadataColumn", "id",
                            col.Attributes["externalMetadataColumnId"]);
                        if (mdcol == null)
                        {
                            var prop = col.GetChildByType("properties")?.GetChildByType("property");
                            var outcol = outputcolumns?.GetChildByTypeAndAttr("outputColumn", "id", prop?.ContentValue);
                            outcolname = outcol?.Attributes["name"];
                        }
                        else
                        {
                            outcolname = mdcol.Attributes["name"];
                        }

                        // Write out the expression
                        SourceWriter.WriteLine($"{indent}    dr[\"{outcolname}\"] = {l};");
                    }

                    // Write the end of this code block
                    SourceWriter.WriteLine($"{indent}    component{Attributes["id"]}.Rows.Add(dr);");
                    SourceWriter.WriteLine($"{indent}}}");
                }
            }
        }

        private void EmitPipelineWriter_TableParam(SsisObject pipeline, string indent)
        {
            SourceWriter.WriteLine();
            SourceWriter.WriteLine($"{indent}// {Attributes["name"]}");

            // Get the connection string GUID: it's connections.connection
            var conn_guid = GetChildByType("connections")?.GetChildByType("connection")?
                .Attributes["connectionManagerID"];
            var connstr = ConnectionWriter.GetConnectionStringName(conn_guid);
            var connprefix = ConnectionWriter.GetConnectionStringPrefix(conn_guid);

            // Report potential problems - can we programmatically convert an OleDb connection into an ADO.NET one?
            var fixup = "";
            if (connprefix == "OleDb")
            {
                SourceWriter.Help(this,
                    "DESSIST had to rewrite an OleDb connection as an ADO.NET connection.  Please check it for correctness.");
                connprefix = "Sql";
                fixup = ".Replace(\"Provider=SQLNCLI10.1;\",\"\")";
            }

            // It's our problem to produce the SQL statement, because this writer uses calculated data!
            var sql = new StringBuilder();
            var colnames = new StringBuilder();
            var varnames = new StringBuilder();
            var paramsetup = new StringBuilder();
            var TableParamCreate = new StringBuilder();
            var TableSetup = new StringBuilder();

            // Create the table parameter insert statement
            var tableparamname = GetFunctionName() + "_WritePipe_TableParam";
            TableParamCreate.AppendFormat(
                "IF EXISTS (SELECT * FROM systypes where name = '{0}') DROP TYPE {0}; {1} CREATE TYPE {0} AS TABLE (",
                tableparamname, Environment.NewLine);

            // Retrieve the names of the columns we're inserting
            var metadata = GetChildByType("inputs")?.GetChildByType("input")?.GetChildByType("externalMetadataColumns");
            var columns = GetChildByType("inputs")?.GetChildByType("input")?.GetChildByType("inputColumns");

            // Okay, let's produce the columns we're inserting
            foreach (var column in columns?.Children ?? new List<SsisObject>())
            {
                var metadataObject = metadata?.GetChildByTypeAndAttr("externalMetadataColumn", "id",
                    column.Attributes["externalMetadataColumnId"]);
                if (metadataObject != null)
                {
                    var cv = new ColumnVariable(metadataObject);

                    // Add to the table parameter create
                    TableParamCreate.Append($"{cv.Name} {cv.SqlDbType()} NULL, ");

                    // List of columns in the insert
                    colnames.Append($"{cv.Name}, ");

                    // List of parameter names in the values clause
                    varnames.Append($"@{cv.Name}, ");

                    // The columns in the in-memory table
                    TableSetup.Append(
                        $"{indent}component{Attributes["id"]}.Columns.Add(\"{cv.Name}\");{Environment.NewLine}");

                    // Find the source column in our lineage data
                    var lineageId = column.Attributes["lineageId"];
                    var lo = pipeline.GetLineageObjectById(lineageId);

                    // Parameter setup instructions
                    if (lo == null)
                    {
                        _project.Log($"I couldn't find lineage column {lineageId}");
                        paramsetup.AppendFormat(
                            $"{indent}            // Unable to find column {lineageId}{Environment.NewLine}");
                    }
                    else
                    {
                        paramsetup.Append($"{indent}    dr[\"{cv.Name}\"] = {lo};{Environment.NewLine}");
                    }
                }
            }

            colnames.Length -= 2;
            varnames.Length -= 2;
            TableParamCreate.Length -= 2;
            TableParamCreate.Append(")");

            // Insert the table parameter create statement in the project
            var sql_tableparam_resource = ProjectWriter.AddSqlResource(GetParentDtsName() + "_WritePipe_TableParam",
                TableParamCreate.ToString());

            // Produce a data set that we're going to process - name it after ourselves
            SourceWriter.WriteLine($"{indent}DataTable component{Attributes["id"]} = new DataTable();");
            SourceWriter.WriteLine(TableSetup.ToString());

            // Check the inputs to see what component we're using as the source
            string? component = null;
            foreach (var incol in GetChildByType("inputs")?.GetChildByType("input")?.GetChildByType("inputColumns")?
                         .Children ?? new List<SsisObject>())
            {
                var input = pipeline.GetLineageObjectById(incol.Attributes["lineageId"]);
                if (component == null)
                {
                    component = input?.DataTableName;
                }
                else
                {
                    if (component != input?.DataTableName)
                    {
                        //SourceWriter.Help(this, "This SSIS pipeline is merging different component tables!");
                        // From closer review, this doesn't look like a problem - it's most likely due to transformations occuring on output of a table
                    }
                }
            }

            // Now fill the table in memory
            SourceWriter.WriteLine();
            SourceWriter.WriteLine($"{indent}// Fill our table parameter in memory");
            SourceWriter.WriteLine($"{indent}for (int row = 0; row < {component}.Rows.Count; row++) {{");
            SourceWriter.WriteLine($"{indent}    DataRow dr = component{Attributes["id"]}.NewRow();");
            SourceWriter.WriteLine(paramsetup.ToString());
            SourceWriter.WriteLine($"{indent}    component{Attributes["id"]}.Rows.Add(dr);");
            SourceWriter.WriteLine($"{indent}}}");

            // Produce the SQL statement
            sql.AppendFormat("INSERT INTO {0} ({1}) SELECT {1} FROM @tableparam",
                GetChildByType("properties")?.GetChildByTypeAndAttr("property", "name", "OpenRowset")?.ContentValue,
                colnames);
            var sql_resource_name = ProjectWriter.AddSqlResource(GetParentDtsName() + "_WritePipe", sql.ToString());

            // Write the using clause for the connection
            SourceWriter.WriteLine();
            SourceWriter.WriteLine($"{indent}// Time to drop this all into the database");
            SourceWriter.WriteLine(
                $"{indent}using (var conn = new {connprefix}Connection(ConfigurationManager.AppSettings[\"{connstr}\"]{fixup})) {{");
            SourceWriter.WriteLine($"{indent}    conn.Open();");

            // Ensure the table parameter type has been created correctly
            SourceWriter.WriteLine();
            SourceWriter.WriteLine($"{indent}    // Ensure the table parameter type has been created successfully");
            SourceWriter.WriteLine($"{indent}    CreateTableParamType(\"{sql_tableparam_resource}\", conn);");

            // Let's use our awesome table parameter style!
            SourceWriter.WriteLine();
            SourceWriter.WriteLine($"{indent}    // Insert all rows at once using fast table parameter insert");
            SourceWriter.WriteLine($"{indent}    using (var cmd = new {connprefix}Command(Resource1.{sql_resource_name}, conn)) {{");

            // Determine the timeout value specified in the pipeline
            var timeout_property =
                GetChildByType("properties")?.GetChildByTypeAndAttr("property", "name", "CommandTimeout");
            if (timeout_property != null)
            {
                var timeout = int.Parse(timeout_property.ContentValue);
                SourceWriter.WriteLine($"{indent}        cmd.CommandTimeout = {timeout};");
            }

            // Insert the table in one swoop
            SourceWriter.WriteLine(
                $"{indent}        SqlParameter param = new SqlParameter(\"@tableparam\", SqlDbType.Structured);");
            SourceWriter.WriteLine($"{indent}        param.Value = component{Attributes["id"]};");
            SourceWriter.WriteLine($"{indent}        param.TypeName = \"{tableparamname}\";");
            SourceWriter.WriteLine($"{indent}        cmd.Parameters.Add(param);");
            SourceWriter.WriteLine($"{indent}        cmd.ExecuteNonQuery();");
            SourceWriter.WriteLine($"{indent}    }}");
            SourceWriter.WriteLine($"{indent}}}");
        }

        private void EmitPipelineWriter(SsisObject pipeline, string indent)
        {
            SourceWriter.WriteLine();
            SourceWriter.WriteLine($"{indent}// {1}", indent, Attributes["name"]);

            // Get the connection string GUID: it's connections.connection
            var conn_guid = GetChildByType("connections")?.GetChildByType("connection")?
                .Attributes["connectionManagerID"];
            var connstr = ConnectionWriter.GetConnectionStringName(conn_guid);
            var connprefix = ConnectionWriter.GetConnectionStringPrefix(conn_guid);

            // Report potential problems - can we programmatically convert an OleDb connection into an ADO.NET one?
            var fixup = "";
            if (connprefix == "OleDb")
            {
                SourceWriter.Help(this,
                    "DESSIST had to rewrite an OleDb connection as an ADO.NET connection.  Please check it for correctness.");
                connprefix = "Sql";
                fixup = $".Replace(\"Provider=SQLNCLI10.1;\",\"\")";
            }

            // It's our problem to produce the SQL statement, because this writer uses calculated data!
            var sql = new StringBuilder();
            var colnames = new StringBuilder();
            var varnames = new StringBuilder();
            var paramsetup = new StringBuilder();

            // Retrieve the names of the columns we're inserting
            var metadata = GetChildByType("inputs")?.GetChildByType("input")?.GetChildByType("externalMetadataColumns");
            var columns = GetChildByType("inputs")?.GetChildByType("input")?.GetChildByType("inputColumns");

            // Okay, let's produce the columns we're inserting
            foreach (var column in columns?.Children ?? new List<SsisObject>())
            {
                var mdcol = metadata?.GetChildByTypeAndAttr("externalMetadataColumn", "id",
                    column.Attributes["externalMetadataColumnId"]);

                // List of columns in the insert
                colnames.Append(mdcol?.Attributes["name"]);
                colnames.Append(", ");

                // List of parameter names in the values clause
                varnames.Append("@");
                varnames.Append(mdcol?.Attributes["name"]);
                varnames.Append(", ");

                // Find the source column in our lineage data
                var lineageId = column.Attributes["lineageId"];
                var lo = pipeline.GetLineageObjectById(lineageId);

                // Parameter setup instructions
                if (lo == null)
                {
                    SourceWriter.Help(this, "I couldn't find lineage column " + lineageId);
                    paramsetup.AppendFormat(@"{0}            // Unable to find column {1}{2}", indent, lineageId,
                        Environment.NewLine);
                }
                else
                {
                    // Is this a string?  If so, forcibly truncate it
                    if (mdcol?.Attributes["dataType"] == "str")
                    {
                        paramsetup.AppendFormat(
                            $"{indent}            cmd.Parameters.Add(new SqlParameter(\"@{mdcol.Attributes["name"]}\", SqlDbType.VarChar, {mdcol.Attributes["length"]}, ParameterDirection.Input, false, 0, 0, null, DataRowVersion.Current, {lo}));");
                    }
                    else if (mdcol?.Attributes["dataType"] == "wstr")
                    {
                        paramsetup.AppendFormat(
                            $"{indent}            cmd.Parameters.Add(new SqlParameter(\"@{mdcol.Attributes["name"]}\", SqlDbType.NVarChar, {mdcol.Attributes["length"]}, ParameterDirection.Input, false, 0, 0, null, DataRowVersion.Current, {lo}));");
                    }
                    else
                    {
                        paramsetup.AppendFormat($"{indent}            cmd.Parameters.AddWithValue(\"@{mdcol?.Attributes["name"]}\",{lo});");
                    }
                }
            }

            colnames.Length -= 2;
            varnames.Length -= 2;

            // Produce the SQL statement
            sql.Append("INSERT INTO ");
            sql.Append(
                GetChildByType("properties")?.GetChildByTypeAndAttr("property", "name", "OpenRowset")?.ContentValue);
            sql.Append(" (");
            sql.Append(colnames);
            sql.Append(") VALUES (");
            sql.Append(varnames);
            sql.Append(")");
            var sql_resource_name = ProjectWriter.AddSqlResource(GetParentDtsName() + "_WritePipe", sql.ToString());

            // Produce a data set that we're going to process - name it after ourselves
            SourceWriter.WriteLine($"{indent}DataTable component{1} = new DataTable();", indent, Attributes["id"]);

            // Write the using clause for the connection
            SourceWriter.WriteLine(
                $"{indent}using (var conn = new {connprefix}Connection(ConfigurationManager.AppSettings[\"{connstr}\"]{fixup})) {{");
            SourceWriter.WriteLine($"{indent}    conn.Open();");

            // TODO: SQL Parameters should go in here

            // Check the inputs to see what component we're using as the source
            string? component = null;
            foreach (var incol in GetChildByType("inputs")?.GetChildByType("input")?.GetChildByType("inputColumns")?
                         .Children ?? new List<SsisObject>())
            {
                var input = pipeline.GetLineageObjectById(incol.Attributes["lineageId"]);
                if (component == null)
                {
                    component = input?.DataTableName;
                }
                else
                {
                    if (component != input?.DataTableName)
                    {
                        //SourceWriter.Help(this, "This SSIS pipeline is merging different component tables!");
                        // From closer review, this doesn't look like a problem - it's most likely due to transformations occuring on output of a table
                    }
                }
            }

            // This is the laziest possible way to do this insert - may want to improve it later
            SourceWriter.WriteLine($"{indent}    for (int row = 0; row < {component}.Rows.Count; row++) {{");
            SourceWriter.WriteLine($"{indent}        using (var cmd = new {connprefix}Command(Resource1.{sql_resource_name}, conn)) {{");
            SourceWriter.WriteLine($"{indent}            cmd.CommandTimeout = 0;");
            SourceWriter.WriteLine(paramsetup.ToString());
            SourceWriter.WriteLine($"{indent}            cmd.ExecuteNonQuery();");
            SourceWriter.WriteLine($"{indent}        }}");
            SourceWriter.WriteLine($"{indent}    }}");
            SourceWriter.WriteLine($"{indent}}}");
        }

        private void EmitPipelineTransform(SsisObject pipeline)
        {
            // Add the columns we're generating
            var transforms = GetChildByType("outputs")?.GetChildByTypeAndAttr("output", "isErrorOut", "false")?
                .GetChildByType("outputColumns")?.Children;

            // Check the inputs to see what component we're using as the source
            var component = "component1";
            var inputcolumns = GetChildByType("inputs")?.GetChildByType("input")?.GetChildByType("inputColumns");
            if (inputcolumns != null)
            {
                foreach (var incol in inputcolumns.Children)
                {
                    var input = pipeline.GetLineageObjectById(incol.Attributes["lineageId"]);
                    if (component == null)
                    {
                        component = input?.DataTableName;
                    }
                    else
                    {
                        if (component != input?.DataTableName)
                        {
                            SourceWriter.Help(this, "This SSIS pipeline is merging different component tables!");
                        }
                    }
                }
            }

            // Let's see if we can generate some code to do these conversions!
            foreach (var outcol in transforms ?? new List<SsisObject>())
            {
                var cv = new ColumnVariable(outcol);
                string? expression = null;

                // Find property "expression"
                if (outcol.Children.Count > 0)
                {
                    foreach (var property in outcol.GetChildByType("properties")?.Children ?? new List<SsisObject>())
                    {
                        if (property.Attributes["name"] == "SourceInputColumnLineageID")
                        {
                            var source_lineage = pipeline.GetLineageObjectById(property.ContentValue);
                            expression = $"Convert.ChangeType({source_lineage?.DataTableName}.Rows[row][\"{source_lineage?.FieldName}\"], typeof({cv.CsharpType()}));";
                        }
                        else if (property.Attributes["name"] == "FastParse")
                        {
                            // Don't need to do anything here
                        }
                        else if (property.Attributes["name"] == "Expression")
                        {
                            // Is this a lineage column?
                            expression = FixExpression(cv.CsharpType(), pipeline._lineageColumns,
                                property.ContentValue, true);
                        }
                        else if (property.Attributes["name"] == "FriendlyExpression")
                        {
                            // This comment is useless - SourceWriter.WriteLine($"{indent}    // {1}", indent, property.ContentValue);
                        }
                        else
                        {
                            _project.Log($"I don't understand the output column property '{property.Attributes["name"]}'");
                        }
                    }

                    // If we haven't been given an explicit expression, just use this
                    if (string.IsNullOrEmpty(expression))
                    {
                        _project.Log("I'm trying to do a transform, but I haven't found an expression to use.");
                    }
                    else
                    {
                        // Put this transformation back into the lineage table for later use!
                        var lo = new LineageObject(outcol.Attributes["lineageId"], expression);
                        pipeline._lineageColumns.Add(lo);
                    }
                }
                else
                {
                    _project.Log( "I'm trying to do a transform, but I don't have any properties to use.");
                }
            }
        }

        private void EmitFlatFilePipelineReader(SsisObject pipeline, string indent)
        {
            // Make sure to include CSV
            ProjectWriter.UseCsvFile = true;

            // Produce a function header
            SourceWriter.WriteLine();
            SourceWriter.WriteLine($"{indent}// {1}", indent, Attributes["name"]);

            // Get the connection string GUID: it's connections.connection
            var conn_guid = GetChildByType("connections")?.GetChildByType("connection")?
                .Attributes["connectionManagerID"];
            var flat_file_obj = _project.GetObjectByGuid(Guid.Parse(conn_guid)).Children[0].Children[0];

            // Some sensible checks
            if (flat_file_obj.Properties["Format"] != "Delimited")
            {
                _project.Log(
                    "The flat file data source is not delimited - DESSIST doesn't have support for this file type!");
            }

            // Retrieve what we need to know about this flat file
            var filename = flat_file_obj.Properties["ConnectionString"];

            // Generate the list of column headers from the connection string object
            var columns = flat_file_obj.Children.Where(child => child.DtsObjectType == "DTS:FlatFileColumn").ToList();

            // Now produce the list of lineage objects from the component
            var outputs = (GetChildByType("outputs")?.GetChildByType("output")?.GetChildByType("outputColumns")?.Children ?? new List<SsisObject>())
                .Where(child => child.DtsObjectType == "outputColumn").ToList();
            if (columns.Count != outputs.Count)
            {
                _project.Log(
                    "The list of columns in this flat file component doesn't match the columns in the data source.");
            }

            // Now pair up all the outputs to generate header columns and lineage objects
            var headers = new StringBuilder("new string[] {");
            for (var i = 0; i < columns.Count; i++)
            {
                var name = columns[i].DtsObjectName;
                headers.Append($"\"{name.EscapeDoubleQuotes()}\", ");
                var lo = new LineageObject(outputs[i].Attributes["lineageId"], "component" + Attributes["id"], name);
                pipeline._lineageColumns.Add(lo);
            }

            headers.Length -= 2;
            headers.Append(" }");

            // This is set to -1 if the column names aren't in the first row in the data file
            var qual = flat_file_obj.Properties["TextQualifier"].FixDelimiter();
            var delim = columns[0].Properties["ColumnDelimiter"].FixDelimiter();
            if (flat_file_obj.Properties["ColumnNamesInFirstDataRow"] == "-1")
            {
                SourceWriter.WriteLine(
                    $"{indent}DataTable component{Attributes["id"]} = CSVFile.CSV.LoadDataTable(\"{filename.EscapeDoubleQuotes()}\", {headers}, true, '{delim}', '{qual}');");
            }
            else
            {
                SourceWriter.WriteLine(
                    $"{indent}DataTable component{Attributes["id"]} = CSVFile.CSV.LoadDataTable(\"{filename.EscapeDoubleQuotes()}\", true, true, '{delim}', '{qual}');");
            }
        }

        private void EmitPipelineReader(SsisObject pipeline, string indent)
        {
            SourceWriter.WriteLine();
            SourceWriter.WriteLine($"{indent}// {Attributes["name"]}");

            // Get the connection string GUID: it's connections.connection
            var conn_guid = GetChildByType("connections")?.GetChildByType("connection")?
                .Attributes["connectionManagerID"];
            var connstr = ConnectionWriter.GetConnectionStringName(conn_guid);
            var connprefix = ConnectionWriter.GetConnectionStringPrefix(conn_guid);

            // Get the SQL statement
            var sql = GetChildByType("properties")?.GetChildByTypeAndAttr("property", "name", "SqlCommand")?.ContentValue;
            if (sql == null)
            {
                var rowset = GetChildByType("properties")?.GetChildByTypeAndAttr("property", "name", "OpenRowset")?
                    .ContentValue;
                if (rowset == null)
                {
                    sql = "COULD NOT FIND SQL STATEMENT";
                    SourceWriter.Help(pipeline, $"Could not find SQL for {Attributes["name"]} in {_dtsId}");
                }
                else
                {
                    sql = "SELECT * FROM " + rowset;
                }
            }

            var sql_resource_name = ProjectWriter.AddSqlResource(GetParentDtsName() + "_ReadPipe", sql);

            // Produce a data set that we're going to process - name it after ourselves
            SourceWriter.WriteLine($"{indent}DataTable component{1} = new DataTable();", indent, Attributes["id"]);

            // Keep track of the lineage of all of our output columns 
            // TODO: Handle error output columns
            foreach (var outcol in GetChildByType("outputs")?.GetChildByTypeAndAttr("output", "isErrorOut", "false")?
                         .GetChildByType("outputColumns")?.Children ?? new List<SsisObject>())
            {
                var lo = new LineageObject(outcol.Attributes["lineageId"], "component" + Attributes["id"],
                    outcol.Attributes["name"]);
                pipeline._lineageColumns.Add(lo);
            }

            // Write the using clause for the connection
            SourceWriter.WriteLine(
                $"{indent}using (var conn = new {connprefix}Connection(ConfigurationManager.AppSettings[\"{connstr}\"])) {{");
            SourceWriter.WriteLine($"{indent}    conn.Open();");
            SourceWriter.WriteLine($"{indent}    using (var cmd = new {connprefix}Command(Resource1.{sql_resource_name}, conn)) {{");

            // Determine the timeout value specified in the pipeline
            var timeout_property =
                GetChildByType("properties")?.GetChildByTypeAndAttr("property", "name", "CommandTimeout");
            if (timeout_property != null)
            {
                var timeout = int.Parse(timeout_property.ContentValue);
                SourceWriter.WriteLine($"{indent}        cmd.CommandTimeout = {1};", indent, timeout);
            }

            // Okay, let's load the parameters
            var paramlist = GetChildByType("properties")?.GetChildByTypeAndAttr("property", "name", "ParameterMapping");
            if (paramlist?.ContentValue != null)
            {
                var p = paramlist.ContentValue.Split(';');
                var paramnum = 0;
                foreach (var oneparam in p)
                {
                    if (!string.IsNullOrEmpty(oneparam))
                    {
                        var parts = oneparam.Split(',');
                        var g = Guid.Parse(parts[1]);

                        // Look up this GUID - can we find it?
                        var v = _project.GetObjectByGuid(g);
                        if (connprefix == "OleDb")
                        {
                            SourceWriter.WriteLine($"{indent}        cmd.Parameters.Add(new OleDbParameter(\"@p{paramnum}\",{v?.DtsObjectName}));");
                        }
                        else
                        {
                            SourceWriter.WriteLine($"{indent}        cmd.Parameters.AddWithValue(\"@{parts[0]}\",{v?.DtsObjectName});");
                        }
                    }

                    paramnum++;
                }
            }

            // Finish up the pipeline reader
            SourceWriter.WriteLine($"{indent}        {connprefix}DataReader dr = cmd.ExecuteReader();");
            SourceWriter.WriteLine($"{indent}        component{Attributes["id"]}.Load(dr);");
            SourceWriter.WriteLine($"{indent}        dr.Close();");
            SourceWriter.WriteLine($"{indent}    }}");
            SourceWriter.WriteLine($"{indent}}}");
        }


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

        private static string FixExpression(string expected_type, List<LineageObject> list, string expression,
            bool inline)
        {
            var ed = new ExpressionData(expected_type, list, expression);
            return ed.ToCSharp(inline);
        }

        private string GetScopeVariables(bool include_type)
        {
            // Do we have any variables to pass?
            var p = new StringBuilder();
            if (include_type)
            {
                foreach (var vd in _scopeVariables)
                {
                    p.AppendFormat("ref {0} {1}, ", vd.CSharpType, vd.VariableName);
                }
            }
            else
            {
                foreach (var vd in _scopeVariables)
                {
                    p.AppendFormat("ref {0}, ", vd.VariableName);
                }
            }

            if (p.Length > 0) p.Length -= 2;
            return p.ToString();
        }

        public string GetFunctionName()
        {
            if (_functionName == null)
            {
                _functionName = StringUtilities.Uniqueify(GetParentDtsName(), _project.FunctionNames);
            }

            return _functionName;
        }

        public string GetFolderName()
        {
            if (_folderName == null)
            {
                _folderName = StringUtilities.Uniqueify(GetParentDtsName(), _project.FolderNames);
            }

            return _folderName;
        }
        public Guid GetNearestGuid()
        {
            var o = this;
            while (o != null && (o._dtsId == Guid.Empty))
            {
                o = o.Parent;
            }

            if (o != null) return o._dtsId;
            return Guid.Empty;
        }

        /// <summary>
        /// Read in a DTS property from the XML stream
        /// </summary>
        /// <param name="element"></param>
        public void ReadDtsProperty(XmlElement element)
        {
            var prop_name =
                (from XmlAttribute xa in element.Attributes
                    where string.Equals(xa.Name, "DTS:Name", StringComparison.CurrentCultureIgnoreCase)
                    select xa.Value).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(prop_name))
            {
                SetProperty(prop_name, element.InnerText);
            }
        }
    }
}