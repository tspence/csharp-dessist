/*
 * 2012-2015 Ted Spence, http://tedspence.com
 * License: http://www.apache.org/licenses/LICENSE-2.0 
 * Home page: https://github.com/tspence/csharp-dessist
 */
using System.Text;
using Dessist.Writers;

// ReSharper disable IdentifierTypo

namespace Dessist
{
    public class ProjectWriter
    {
        public string AppFolder;

        public List<string> ProjectFiles = new List<string>();
        public List<string> AllFiles = new List<string>();
        public List<string> DllFiles = new List<string>();

        public bool UseSqlServerManagementObjects = false;
        public bool UseCsvFile = false;
        private static Dictionary<string, string> _resources = new Dictionary<string, string>();
        //private List<string> _resources = new List<string>();
        private readonly SqlCompatibilityType _sqlMode;


        private SsisProject _project;

        public ProjectWriter(SsisProject project, SqlCompatibilityType sqlMode, bool useSqlServerManagementObjects, string output_folder)
        {
            _project = project;
            _sqlMode = sqlMode;
            Directory.CreateDirectory(output_folder);
            AppFolder = output_folder;
            UseSqlServerManagementObjects = useSqlServerManagementObjects;
        }

        /// <summary>
        /// Produce C# files that replicate the functionality of an SSIS package
        /// </summary>
        public void ProduceSsisDotNetPackage()
        {
            WriteAppConfig();
            WriteResourceAndProjectFile(AppFolder, _project.Name);
        }

        /// <summary>
        /// Write an app.config file with the specified settings
        /// </summary>
        private void WriteAppConfig()
        {
            var appConfigFilename = Path.Combine(AppFolder, "app.config");
            using (var sw = new StreamWriter(appConfigFilename, false, Encoding.UTF8))
            {
                sw.WriteLine(@"<?xml version=""1.0"" encoding=""utf-8""?>");
                sw.WriteLine(@"<configuration>");
                sw.WriteLine(@"  <startup useLegacyV2RuntimeActivationPolicy=""true"">");
                sw.WriteLine(@"    <supportedRuntime version=""v4.0""/>");
                sw.WriteLine(@"  </startup>");
                sw.WriteLine(@"  <appSettings>");

                // Write each one in turn
                foreach (var connstr in _project.ConnectionStrings())
                {
                    var s = "Not Found";
                    var v = connstr.GetChildByType("DTS:ObjectData");
                    if (v != null)
                    {

                        // Look for a SQL Connection string
                        var v2 = v.GetChildByType("DTS:ConnectionManager");
                        if (v2 != null)
                        {
                            v2.Properties.TryGetValue("ConnectionString", out s);
                        }
                        else
                        {
                            v2 = v.GetChildByType("SmtpConnectionManager");
                            if (v2 != null)
                            {
                                v2.Attributes.TryGetValue("ConnectionString", out s);
                            }
                            else
                            {
                                _project.Log("Missing SmtpConnectionManager value");
                            }
                        }
                    }

                    sw.WriteLine($"    <add key=\"{connstr.DtsObjectName}\" value=\"{s}\" />");
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
         public string? GetConnectionStringName(string? conn_guid_str)
         {
             var connobj = _project.GetObjectByGuid(conn_guid_str);
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
         public string GetConnectionStringPrefix(string? conn_guid_str)
         {
             var connobj = _project.GetObjectByGuid(Guid.Parse(conn_guid_str));
             if (connobj == null) {
                 return "Sql";
             }
             var objecttype = connobj.Properties["CreationName"];
             if (objecttype.StartsWith("OLEDB")) {
                 return "OleDb";
             } else if (objecttype.StartsWith("ADO.NET:System.Data.SqlClient.SqlConnection")) {
                 return "Sql";
             } else {
                 _project.Log($"I don't understand the database connection type {objecttype}");
             }
             return "";
         }
        
         /// <summary>
         /// Write a program file that has all the major executable instructions as functions
         /// </summary>
         private void WriteProgram()
         {
             var filename = Path.Combine(AppFolder, "program.cs");
             using (var sw = new StreamWriter(filename, false, Encoding.UTF8))
             {
                 var smo_using = "";
                 var tableparamstatic = "";

                 // Are we using SMO mode?
                 if (UseSqlServerManagementObjects)
                 {
                     smo_using = Resource1.SqlSmoUsingTemplate;
                 }

                 // Are we using SQL 2008 mode?
                 if (_sqlMode == SqlCompatibilityType.SQL2008)
                 {
                     tableparamstatic = Resource1.TableParameterStaticTemplate;
                 }

                 // Write the header in one fell swoop
                 sw.Write(
                     Resource1.ProgramHeaderTemplate
                         .Replace("@@USINGSQLSMO@@", smo_using)
                         .Replace("@@NAMESPACE@@", _project.Namespace)
                         .Replace("@@TABLEPARAMSTATIC@@", tableparamstatic)
                         .Replace("@@MAINFUNCTION@@", _project.Functions().FirstOrDefault()?.GetFunctionName() ?? "UnknownMainFunction")
                 );

                 // Write each variable out as if it's a global
                 foreach (var v in _project.Variables())
                 {
                     sw.Write(EmitVariable(v, true));
                 }

                 sw.WriteLine();
                 sw.WriteLine();

                 // Write each executable out as a function
                 foreach (var v in _project.Functions())
                 {
                     sw.Write(EmitFunction(v, new List<ProgramVariable>()));
                 }

                 sw.WriteLine(Resource1.ProgramFooterTemplate);
             }
         }
        
         private CodeBlock EmitScriptProject(SsisObject o)
         {
             var code = new CodeBlock();
             
             // Find the script object child
             var script = o.GetChildByType("DTS:ObjectData")?.GetChildByType("ScriptProject");

             // Create a folder for this script
             var project_folder = Path.Combine(AppFolder, o.GetFolderName());
             Directory.CreateDirectory(project_folder);

             // Extract all the individual script files in this script
             foreach (var child in script?.Children ?? new List<SsisObject>()) {
                 var fn = project_folder + child.Name;
                 var dir = Path.GetDirectoryName(fn);
                 Directory.CreateDirectory(dir);

                 if (child.DtsObjectType == "BinaryItem") {
                     var contents = System.Convert.FromBase64String(child?.ContentValue ?? "");
                     File.WriteAllBytes(fn, contents);
                 } else if (child.DtsObjectType == "ProjectItem") {
                     File.WriteAllText(fn, child.ContentValue);
                 }

                 // Handle DLL files specially - they are binary!  Oh yeah base64 encoded
                 if (fn.EndsWith(".dll")) {
                     DllFiles.Add(fn);

                     // Note this as a potential problem
                     _project.Log("The Visual Basic project {child.Name} was embedded in the DTSX project.  Visual Basic code cannot be automatically converted.");

                     // Show the user that this is how the script should be executed, if they want to fix it
                     var scriptTaskName = Path.GetFileNameWithoutExtension(fn).Replace("scripttask", "ScriptTask");
                     code.Add($"//{scriptTaskName}.ScriptMain sm = new {scriptTaskName}.ScriptMain();");
                     code.Add("//sm.Main();");

                     // Is this a project file?
                 } else if (fn.EndsWith(".vbproj") || fn.EndsWith(".csproj")) {
                     ProjectFiles.Add(fn);
                 } else {
                     AllFiles.Add(fn);
                 }
             }

             return code;
         }

         public string AddSqlResource(string name, string resource)
         {
             var munged_name = FixResourceName(name);
             _resources[munged_name] = resource;
             return munged_name;
         }


         /// <summary>
         /// Ensure a unique resource name for this resource
         /// </summary>
         /// <param name="name"></param>
         /// <returns></returns>
         private string FixResourceName(string name)
         {
             var newName = (name ?? "UnnamedStatement").CleanNamespaceName();
             var resourceNames = _resources.Keys.ToList();
             var uniqueName = StringUtilities.Uniqueify(newName, resourceNames);
             _resources[uniqueName] = "";
             return uniqueName;
         }

         public void WriteResourceAndProjectFile(string folder, string appname)
         {
             // Ensure resources folder exists
             var resfolder = Path.Combine(folder, "Resources");
             Directory.CreateDirectory(resfolder);
             var propfolder = Path.Combine(folder, "Properties");
             Directory.CreateDirectory(propfolder);

             // Let's produce the template of the resources
             var resfile = new StringBuilder();
             var prjfile = new StringBuilder();
             var desfile = new StringBuilder();
             foreach (var kvp in _resources) {
                 resfile.Append(Resource1.IndividualResourceSnippet.Replace("@@RESNAME@@", kvp.Key));
                 prjfile.Append(Resource1.IndividualResourceProjectSnippet.Replace("@@RESNAME@@", kvp.Key));
                 desfile.Append(Resource1.IndividualResourceDesignerTemplate.Replace("@@RESNAME@@", kvp.Key));

                 // Write this to a file as well
                 File.WriteAllText(Path.Combine(resfolder, kvp.Key + ".sql"), kvp.Value);
             }

             // Iterate through DLLS
             var DllReferences = new StringBuilder();
             foreach (var dll in DllFiles) {
                 DllReferences.Append(
                     Resource1.DllReferenceTemplate
                         .Replace("@@FILENAMEWITHOUTEXTENSION@@", Path.GetFileNameWithoutExtension(dll))
                         .Replace("@@RELATIVEPATH@@", dll.Substring(folder.Length+1)));
             }

             // Spit out the resource file
             File.WriteAllText(Path.Combine(folder, "Resource1.resx"), Resource1.ResourceTemplate.Replace("@@RESOURCES@@", resfile.ToString()));
            
             // Copy the Microsoft SQL Server objects!
             if (UseSqlServerManagementObjects) {
                 DllReferences.Append(@"<Reference Include=""Microsoft.SqlServer.ConnectionInfo, Version=10.0.0.0, Culture=neutral, PublicKeyToken=89845dcd8080cc91, processorArchitecture=MSIL"">
                      <SpecificVersion>False</SpecificVersion>
                      <HintPath>Microsoft.SqlServer.ConnectionInfo.dll</HintPath>
                    </Reference>
                    <Reference Include=""Microsoft.SqlServer.Management.Sdk.Sfc, Version=10.0.0.0, Culture=neutral, PublicKeyToken=89845dcd8080cc91, processorArchitecture=MSIL"">
                      <SpecificVersion>False</SpecificVersion>
                      <HintPath>Microsoft.SqlServer.Management.Sdk.Sfc.dll</HintPath>
                    </Reference>
                    <Reference Include=""Microsoft.SqlServer.Smo, Version=10.0.0.0, Culture=neutral, PublicKeyToken=89845dcd8080cc91, processorArchitecture=MSIL"">
                      <SpecificVersion>False</SpecificVersion>
                      <HintPath>Microsoft.SqlServer.Smo.dll</HintPath>
                    </Reference>");
             }

             // Copy the CSV stuff if necessary
             if (UseCsvFile) {
                 DllReferences.Append(@"<Reference Include=""CSVFile, Version=1.0.0.0, Culture=neutral, processorArchitecture=MSIL"">
                      <SpecificVersion>False</SpecificVersion>
                      <HintPath>CSVFile.dll</HintPath>
                    </Reference>");
             }

             // Resource needs a designer file too!
             var designer =
                 Resource1.ResourceDesignerTemplate
                     .Replace("@@APPNAME@@", appname)
                     .Replace("@@RESOURCES@@", desfile.ToString());
             File.WriteAllText(Path.Combine(folder, "Resource1.Designer.cs"), designer);

             // Spit out the project file
             var proj_guid = Guid.NewGuid();
             var project =
                 Resource1.ProjectTemplate
                     .Replace("@@RESOURCES@@", prjfile.ToString())
                     .Replace("@@DLLS@@", DllReferences.ToString())
                     .Replace("@@APPNAME@@", appname)
                     .Replace("@@PROJGUID@@", proj_guid.ToString().ToUpper());
             File.WriteAllText(Path.Combine(folder, appname + ".csproj"), project);

             // Spit out the solution file
             var sln_guid = Guid.NewGuid();
             var solution =
                 Resource1.SolutionTemplate
                     .Replace("@@PROJGUID@@", proj_guid.ToString().ToUpper())
                     .Replace("@@SOLUTIONGUID@@", sln_guid.ToString().ToUpper())
                     .Replace("@@APPNAME@@", appname);
             File.WriteAllText(Path.Combine(folder, appname + ".sln"), solution);

             // Spit out the assembly file
             var asy_guid = Guid.NewGuid();
             var assembly =
                 Resource1.AssemblyTemplate
                     .Replace("@@ASSEMBLYGUID@@", asy_guid.ToString())
                     .Replace("@@APPNAME@@", appname);
             File.WriteAllText(Path.Combine(propfolder, "AssemblyInfo.cs"), assembly);

             // Spit out the assembly file
             File.WriteAllText(Path.Combine(folder, "RecursiveTimeLog.cs"), 
                 Resource1.RecursiveTimeLog
                     .Replace("@@NAMESPACE@@", appname));

             // Write out the help notes
             _project.Log("Decompilation completed.");
             if (SourceWriter._help_messages.Count > 0) {
                 var helpfile = Path.Combine(folder, "ImportErrors.txt");
                 File.Delete(helpfile);
                 using (var sw = new StreamWriter(helpfile)) {
                     foreach (var help in SourceWriter._help_messages) {
                         sw.WriteLine(help);
                     }
                 }
                 _project.Log($"{SourceWriter._help_messages.Count} import errors encountered.");
                 if (SourceWriter._help_messages.Count > 0)
                 {
                     _project.Log($"Import errors written to {helpfile}");
                     _project.Log();
                     _project.Log("Please consider opening an issue on GitHub to report these import errors to  the DESSIST team.");
                     _project.Log("Visit our website online here:");
                     _project.Log("    https://github.com/tspence/csharp-dessist");
                 }
             }
         }
        
         /// <summary>
         /// Produce this variable to the current stream
         /// </summary>
         /// <param name="sqlMode"></param>
         /// <param name="indent"></param>
         /// <param name="scope_variables"></param>
         internal CodeBlock EmitFunction(SsisObject func, List<ProgramVariable> scope_variables)
         {
             var code = new CodeBlock();
             func._scopeVariables.AddRange(scope_variables);

             // Header and comments
             code.Add();
             if (!string.IsNullOrEmpty(func.Description))
             {
                 code.Add($"/// <summary>");
                 code.Add($"/// {func.Description}");
                 code.Add($"/// </summary>");
             }

             // Function intro
             code.Add($"public static void {func.GetFunctionName()}({func.GetScopeVariables(true)})");
             code.Add($"{{");
             code.Add($"    timer.Enter(\"{func.GetFunctionName()}\");");

             // What type of executable are we?  Let's check if special handling is required
             var exec_type = func.Attributes["DTS:ExecutableType"];

             // Child script project - Emit it as a sub-project within the greater solution!
             if (exec_type.StartsWith("Microsoft.SqlServer.Dts.Tasks.ScriptTask.ScriptTask"))
             {
                 code.Merge(EmitScriptProject(func), 4);

                 // Basic SQL command
             }
             else if (exec_type.StartsWith("Microsoft.SqlServer.Dts.Tasks.ExecuteSQLTask.ExecuteSQLTask"))
             {
                 code.Merge(EmitSqlTask(func), 4);

                 // Basic "SEQUENCE" construct - just execute things in order!
             }
             else if (exec_type.StartsWith("STOCK:SEQUENCE"))
             {
                 code.Merge(EmitChildObjects(func), 4);

                 // Handle "FOR" and "FOREACH" loop types
             }
             else if (exec_type == "STOCK:FORLOOP")
             {
                 code.Merge(EmitForLoop(func), 4);
             }
             else if (exec_type == "STOCK:FOREACHLOOP")
             {
                 code.Merge(EmitForEachLoop(func), 4);
             }
             else if (exec_type == "SSIS.Pipeline.2")
             {
                 code.Merge(EmitPipeline(func), 4);
             }
             else if (exec_type.StartsWith("Microsoft.SqlServer.Dts.Tasks.SendMailTask.SendMailTask"))
             {
                 code.Merge(EmitSendMailTask(func), 4);
             }
             else
             {
                 _project.Log($"I don't yet know how to handle {exec_type}");
             }

             // TODO: Is there an exception handler?  How many types of event handlers are there?

             // End of function
             code.Add("    timer.Leave();");
             code.Add("}");

             // Now emit any other functions that are chained into this
             foreach (var child in func.Children)
             {
                 if (child.DtsObjectType == "DTS:Executable")
                 {
                     code.Merge(EmitFunction(child, scope_variables), 4);
                 }
             }

             return code;
         }

         private CodeBlock EmitSendMailTask(SsisObject task)
         {
             var code = new CodeBlock();
             
             // Navigate to our object data
             var mail = task.GetChildByType("DTS:ObjectData")?.GetChildByType("SendMailTask:SendMailTaskData");

             code.Add($"MailMessage message = new MailMessage();");
             code.Add($"message.To.Add(\"{mail?.Attributes["SendMailTask:To"]}\");");
             code.Add($"message.Subject = \"{mail?.Attributes["SendMailTask:Subject"]}\";");
             code.Add($"message.From = new MailAddress(\"{mail?.Attributes["SendMailTask:From"]}\");");

             // Handle CC/BCC if available
             if (mail.Attributes.TryGetValue("SendMailTask:CC", out var addr) && !string.IsNullOrEmpty(addr))
             {
                 code.Add($"message.CC.Add(\"{addr}\");");
             }

             if (mail.Attributes.TryGetValue("SendMailTask:BCC", out addr) && !string.IsNullOrEmpty(addr))
             {
                 code.Add($"message.Bcc.Add(\"{addr}\");");
             }

             // Process the message source
             var sourcetype = mail.Attributes["SendMailTask:MessageSourceType"];
             if (sourcetype == "Variable")
             {
                 code.Add($"message.Body = {mail.Attributes["SendMailTask:MessageSource"].FixVariableName()};");
             }
             else if (sourcetype == "DirectInput")
             {
                 code.Add($"message.Body = @\"{mail.Attributes["SendMailTask:MessageSource"].EscapeDoubleQuotes()}\";");
             }
             else
             {
                 _project.Log($"I don't understand the SendMail message source type '{sourcetype}'");
             }

             // Get the SMTP configuration name
             code.Add(
                 $"using (var smtp = new SmtpClient(ConfigurationManager.AppSettings[\"{_project.GetObjectByGuid(mail.Attributes["SendMailTask:SMTPServer"])?.DtsObjectName}\"])) {{");
             code.Add($"    smtp.Send(message);");
             code.Add("}");
             return code;
         }

         private CodeBlock EmitSqlTask(SsisObject obj)
         {
             return EmitChildObjects(obj);
         }

         private CodeBlock EmitChildObjects(SsisObject parent)
         {
             var code = new CodeBlock();

             // To handle precedence data correctly, first make a list of encumbered children
             var modified_children = new List<SsisObject>();
             modified_children.AddRange(parent.Children);

             // Write comments for the precedence data - we'll eventually have to handle this
             var precedence = new List<PrecedenceData>();
             foreach (var o in parent.Children)
             {
                 if (o.DtsObjectType == "DTS:PrecedenceConstraint")
                 {
                     var pd = new PrecedenceData(parent.Project, o);

                     // Does this precedence data affect any children?  Find it and move it
                     var c = (from obj in modified_children where obj.DtsId == pd.AfterGuid select obj)
                         .FirstOrDefault();
                     modified_children.Remove(c);

                     // Add it to the list
                     precedence.Add(pd);
                 }
             }

             if (modified_children.Count > 0)
             {
                 code.Add("// These calls have no dependencies");

                 // Function body
                 foreach (var o in modified_children)
                 {
                     code.Merge(PrecedenceChain(o, precedence), 0);
                 }
             }

             return code;
         }

         private CodeBlock PrecedenceChain(SsisObject prior_obj, List<PrecedenceData> precedence)
         {
             var code = new CodeBlock();
             code.Merge(EmitOneChild(prior_obj), 0);

             // We just executed "prior_obj" - find what objects it causes to be triggered
             var triggered = (from PrecedenceData pd in precedence where pd.BeforeGuid == prior_obj.DtsId select pd);

             // Iterate through each of these
             foreach (var pd in triggered)
             {
                 // Write a comment
                 code.Add();
                 code.Add($"// {pd}");

                 // Is there an expression?
                 if (!string.IsNullOrEmpty(pd.Expression))
                 {
                     code.Add($"if ({prior_obj.FixExpression("System.Boolean", prior_obj._lineageColumns, pd.Expression, true)}) {{");
                     code.Merge(PrecedenceChain(pd.Target, precedence), 4);
                     code.Add("}");
                 }
                 else
                 {
                     code.Merge(PrecedenceChain(pd.Target, precedence), 4);
                 }
             }

             return code;
         }

         private CodeBlock EmitOneChild(SsisObject childobj)
         {
             var code = new CodeBlock();
             
             // Is this a dummy "Object Data" thing?  If so ignore it and delve deeper
             if (childobj.DtsObjectType == "DTS:ObjectData")
             {
                 childobj = childobj.Children[0];
             } else
             if (childobj.DtsObjectType == "DTS:Variable")
             {
                 code.Merge(EmitVariable(childobj, false), 0);
             }
             else if (childobj.DtsObjectType == "DTS:Executable")
             {
                 code.Merge(EmitFunctionCall(childobj, childobj.GetScopeVariables(false)), 0);
             }
             else if (childobj.DtsObjectType == "SQLTask:SqlTaskData")
             {
                 code.Merge(EmitSqlStatement(childobj), 0);
             }
             else if (childobj.DtsObjectType == "pipeline")
             {
                 code.Merge(EmitPipeline(childobj), 0);
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
                 _project.Log($"I don't yet know how to handle {childobj.DtsObjectType}");
             }

             return code;
         }

         private CodeBlock EmitForEachVariableMapping(SsisObject mapping)
         {
             var code = new CodeBlock();
             var varname = mapping.Properties["VariableName"].FixVariableName();
             var vd = _project.GetVariable(varname);
             code.Add($"{varname} = ({vd?.CSharpType})iter.ItemArray[{mapping.Properties["ValueIndex"]}];");
             return code;
         }

         private CodeBlock EmitForEachLoop(SsisObject loop)
         {
             var code = new CodeBlock();
             // Retrieve the three settings from the for loop
             var iterator = loop.GetChildByType("DTS:ForEachEnumerator")?.GetChildByType("DTS:ObjectData")?.Children[0]
                 .Attributes["VarName"].FixVariableName();

             // Write it out - I'm assuming this is a data table for now
             code.Add("int current_row_num = 0;");
             code.Add($"foreach (DataRow iter in {iterator}.Rows) {{");
             code.Add("    Console.WriteLine($\"{{DateTime.Now}} Loop: On row {{++current_row_num}} of {{iterator}}.Rows.Count);");
             code.Add();
             code.Add("// Setup all variable mappings");

             // Do all the iteration mappings first
             foreach (var childobj in loop.Children.Where(childobj => childobj.DtsObjectType == "DTS:ForEachVariableMapping"))
             {
                 code.Merge(EmitForEachVariableMapping(childobj), 4);
             }

             code.Add();

             // Other interior objects and tasks
             code.Merge(EmitChildObjects(loop), 4);

             // Close the loop
             code.Add("}");
             return code;
         }

         private CodeBlock EmitForLoop(SsisObject obj)
         {
             var code = new CodeBlock();
             
             // Retrieve the three settings from the for loop
             var init = System.Net.WebUtility.HtmlDecode(obj.Properties["InitExpression"]).Replace("@", "");
             var eval = System.Net.WebUtility.HtmlDecode(obj.Properties["EvalExpression"]).Replace("@", "");
             var assign = System.Net.WebUtility.HtmlDecode(obj.Properties["AssignExpression"]).Replace("@", "");

             // Write it out
             code.Add($"for ({init};{eval};{assign}) {{");

             // Inner stuff ?
             code.Merge(EmitChildObjects(obj), 4);

             // Close the loop
             code.Add("}");
             return code;
         }

         /// <summary>
         /// Write out a function call
         /// </summary>
         /// <param name="scope_variables"></param>
         private CodeBlock EmitFunctionCall(SsisObject obj, string scope_variables)
         {
             var code = new CodeBlock();
             if (obj.Properties["Disabled"] == "-1")
             {
                 code.Add("// SSIS records this function call is disabled");
                 code.Add($"// {obj.GetFunctionName()}({scope_variables});");
             }
             else
             {
                 code.Add($"{obj.GetFunctionName()}({scope_variables});");
             }

             return code;
         }

         /// <summary>
         /// Write out an SQL statement
         /// </summary>
         /// <param name="obj"></param>
         private CodeBlock EmitSqlStatement(SsisObject obj)
         {
             var code = new CodeBlock();
             
             // Retrieve the connection string object
             var conn_guid = obj.Attributes["SQLTask:Connection"];
             var connstr = GetConnectionStringName(conn_guid);
             var connprefix = GetConnectionStringPrefix(conn_guid);
             var is_sqlcmd = StringUtilities.IsSql(obj.Attributes["SQLTask:SqlStatementSource"]);

             // Report potential problems - can we programmatically convert an OleDb connection into an ADO.NET one?
             var fixup = "";
             if (connprefix == "OleDb")
             {
                 _project.Log(
                     "DESSIST had to rewrite an OleDb connection as an ADO.NET connection.  Please check it for correctness.");
                 connprefix = "Sql";
                 fixup = @".FixupOleDb()";
             }

             // Retrieve the SQL String and put it in a resource
             var raw_sql = obj.Attributes["SQLTask:SqlStatementSource"];

             // Do we need to forcibly convert this code to regular SQL?  This is dangerous, but might be okay if we know it's safe!
             if (is_sqlcmd && !UseSqlServerManagementObjects)
             {
                 raw_sql = raw_sql.Replace("\nGO", "\n;");
                 _project.Log($"Forcibly converted the SQL server script '{raw_sql}' (containing multiple statements) to raw SQL (single statement).  Check that this is safe!");
                 is_sqlcmd = false;
             }

             var sql_attr_name = AddSqlResource(obj.GetParentDtsName(), raw_sql);

             // Do we have a result binding?
             var binding = obj.GetChildByType("SQLTask:ResultBinding");

             // Are we going to return anything?  Prepare a variable to hold it
             if (obj.Attributes.ContainsKey("SQLTask:ResultType") &&
                 obj.Attributes["SQLTask:ResultType"] == "ResultSetType_SingleRow")
             {
                 code.Add($"object result = null;");
             }
             else if (binding != null)
             {
                 code.Add($"DataTable result = null;");
             }

             code.Add($"Console.WriteLine(\"{{indent}} SQL: {sql_attr_name}\", DateTime.Now);");
             code.Add();

             // Open the connection
             code.Add(
                 $"using (var conn = new {connprefix}Connection(ConfigurationManager.AppSettings[\"{connstr}\"]{fixup})) {{");
             code.Add($"    conn.Open();");

             // Does this SQL statement include any nested "GO" commands?  Let's make a simple call
             string sql_variable_name;
             if (is_sqlcmd)
             {
                 code.Add();
                 code.Add("    // This SQL statement is a compound statement that must be run from the SQL Management object");
                 code.Add("    ServerConnection svrconn = new ServerConnection(conn);");
                 code.Add("    Server server = new Server(svrconn);");
                 code.Add("    server.ConnectionContext.SqlExecutionModes = SqlExecutionModes.CaptureSql;");
                 code.Add($"    server.ConnectionContext.ExecuteNonQuery(Resource1.{sql_attr_name});");
                 code.Add("    foreach (string s in server.ConnectionContext.CapturedSql.Text) {{");
                 sql_variable_name = "s";
             }
             else
             {
                 sql_variable_name = "Resource1." + sql_attr_name;
             }

             // Write the using clause for the connection
             code.Add($"    using (var cmd = new {connprefix}Command({sql_variable_name}, conn)) {{");
             if (obj.Attributes.ContainsKey("SQLTask:TimeOut"))
             {
                 code.Add($"        cmd.CommandTimeout = {obj.Attributes["SQLTask:TimeOut"]};");
             }

             // Handle our parameter binding
             foreach (var childobj in obj.Children.Where(childobj => childobj.DtsObjectType == "SQLTask:ParameterBinding"))
             {
                 code.Add($"        cmd.Parameters.AddWithValue(\"{childobj.Attributes["SQLTask:ParameterName"]}\", {childobj.Attributes["SQLTask:DtsVariableName"].FixVariableName()});");
             }

             // What type of variable reading are we doing?
             if (obj.Attributes.ContainsKey("SQLTask:ResultType") &&
                 obj.Attributes["SQLTask:ResultType"] == "ResultSetType_SingleRow")
             {
                 code.Add($"        result = cmd.ExecuteScalar();");
             }
             else if (binding != null)
             {
                 code.Add($"        {connprefix}DataReader dr = cmd.ExecuteReader();");
                 code.Add($"        result = new DataTable();");
                 code.Add($"        result.Load(dr);");
                 code.Add($"        dr.Close();");
             }
             else
             {
                 code.Add($"        cmd.ExecuteNonQuery();");
             }

             // Finish up the SQL call
             code.Add("    }");
             code.Add("}");

             // Do work with the bound result
             if (binding != null)
             {
                 var varname = binding.Attributes["SQLTask:DtsVariableName"];
                 var fixedname = varname.FixVariableName();
                 var vd = _project.GetVariable(fixedname);

                 // Emit our binding
                 code.Add();
                 code.Add($"// Bind results to {varname}");
                 if (vd?.CSharpType == "DataTable")
                 {
                     code.Add($"{fixedname} = result;");
                 }
                 else
                 {
                     code.Add($"{fixedname} = ({vd?.CSharpType})result;");
                 }
             }

             // Clean up properly if this was a compound statement
             if (is_sqlcmd)
             {
                 code.Add($"}}");
             }

             return code;
         }

         private CodeBlock? EmitPipeline(SsisObject pipeline)
         {
             var code = new CodeBlock();
             
             // Find the component container
             var component_container =
                 pipeline.GetChildByType("DTS:ObjectData")?.GetChildByType("pipeline")?.GetChildByType("components");
             if (component_container == null)
             {
                 _project.Log("Unable to find DTS:ObjectData/pipeline/components SSIS components!");
                 return null;
             }

             // Keep track of original components
             var components = new List<SsisObject>();
             components.AddRange(component_container.Children);

             // Produce all the readers
             foreach (var child in component_container.GetChildrenByTypeAndAttr("componentClassID",
                          "{BCEFE59B-6819-47F7-A125-63753B33ABB7}"))
             {
                 code.Merge(EmitPipelineReader(pipeline), 0);
                 components.Remove(child);
             }

             // These are the "flat file source" readers
             foreach (var child in component_container.GetChildrenByTypeAndAttr("componentClassID",
                          "{5ACD952A-F16A-41D8-A681-713640837664}"))
             {
                 code.Merge(EmitFlatFilePipelineReader(pipeline), 0);
                 components.Remove(child);
             }

             // Iterate through all transformations
             foreach (var child in component_container.GetChildrenByTypeAndAttr("componentClassID",
                          "{BD06A22E-BC69-4AF7-A69B-C44C2EF684BB}"))
             {
                 code.Merge(EmitPipelineTransform(pipeline), 0);
                 components.Remove(child);
             }

             // Iterate through all transformations - this is basically the same thing but this time it uses "expression" rather than "type conversion"
             foreach (var child in component_container.GetChildrenByTypeAndAttr("componentClassID",
                          "{2932025B-AB99-40F6-B5B8-783A73F80E24}"))
             {
                 code.Merge(EmitPipelineTransform(pipeline), 0);
                 components.Remove(child);
             }

             // Iterate through unions
             foreach (var child in component_container.GetChildrenByTypeAndAttr("componentClassID",
                          "{4D9F9B7C-84D9-4335-ADB0-2542A7E35422}"))
             {
                 code.Merge(EmitPipelineUnion(pipeline), 0);
                 components.Remove(child);
             }

             // Iterate through all multicasts
             foreach (var child in component_container.GetChildrenByTypeAndAttr("componentClassID",
                          "{1ACA4459-ACE0-496F-814A-8611F9C27E23}"))
             {
                 //child.EmitPipelineMulticast(this, indent);
                 code.Add($"// MULTICAST: Using all input and writing to multiple outputs");
                 components.Remove(child);
             }

             // Process all the writers
             foreach (var child in component_container.GetChildrenByTypeAndAttr("componentClassID",
                          "{5A0B62E8-D91D-49F5-94A5-7BE58DE508F0}"))
             {
                 if (_sqlMode == SqlCompatibilityType.SQL2008)
                 {
                     code.Merge(EmitPipelineWriter_TableParam(pipeline), 0);
                 }
                 else
                 {
                     code.Merge(EmitPipelineWriter(pipeline), 0);
                 }

                 components.Remove(child);
             }

             // Report all unknown components
             foreach (var component in components)
             {
                 _project.Log(
                     $"I don't know how to process componentClassID = {component.Attributes["componentClassID"]}");
             }

             return code;
         }
        
         private CodeBlock EmitPipelineUnion(SsisObject pipeline)
         {
             var code = new CodeBlock();
             code.Add();
             code.Add($"// {pipeline.Name}");

             // Create a new datatable
             code.Add($"DataTable component{pipeline.ID} = new DataTable();");

             // Add the columns we're generating
             var transforms = pipeline.GetChildByType("outputs")?.GetChildByTypeAndAttr("output", "isErrorOut", "false")?
                 .GetChildByType("outputColumns")?.Children;
             foreach (var outcol in transforms ?? new List<SsisObject>())
             {
                 var cv = new ColumnVariable(outcol);
                 var lo = new LineageObject(cv.LineageID, "component" + pipeline.ID, cv.Name);
                 pipeline._lineageColumns.Add(lo);

                 // Print out this column
                 code.Add($"component{pipeline.ID}.Columns.Add(new DataColumn(\"{cv.Name}\", typeof({cv.CsharpType()})));");
             }

             // Loop through all the inputs and process them!
             var outputcolumns = pipeline.GetChildByType("outputs")?.GetChildByType("output")?.GetChildByType("outputColumns");
             foreach (var inputtable in pipeline.GetChildByType("inputs")?.Children ?? new List<SsisObject>())
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
                     code.Add($"for (int row = 0; row < {component}.Rows.Count; row++) {{");
                     code.Add($"    DataRow dr = component{pipeline.ID}.NewRow();");

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
                             outcolname = outcol?.Name;
                         }
                         else
                         {
                             outcolname = mdcol.Name;
                         }

                         // Write out the expression
                         code.Add($"    dr[\"{outcolname}\"] = {l};");
                     }

                     // Write the end of this code block
                     code.Add($"    component{pipeline.ID}.Rows.Add(dr);");
                     code.Add($"}}");
                 }
             }

             return code;
         }

         private CodeBlock EmitPipelineWriter_TableParam(SsisObject pipeline)
         {
             var code = new CodeBlock();
             code.Add();
             code.Add($"// {pipeline.Name}");

             // Get the connection string GUID: it's connections.connection
             var conn_guid = pipeline.GetChildByType("connections")?.GetChildByType("connection")?
                 .Attributes["connectionManagerID"];
             var connstr = GetConnectionStringName(conn_guid);
             var connprefix = GetConnectionStringPrefix(conn_guid);

             // Report potential problems - can we programmatically convert an OleDb connection into an ADO.NET one?
             var fixup = "";
             if (connprefix == "OleDb")
             {
                 _project.Log(
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
             var tableparamname = pipeline.GetFunctionName() + "_WritePipe_TableParam";
             TableParamCreate.AppendFormat(
                 "IF EXISTS (SELECT * FROM systypes where name = '{0}') DROP TYPE {0}; {1} CREATE TYPE {0} AS TABLE (",
                 tableparamname, Environment.NewLine);

             // Retrieve the names of the columns we're inserting
             var metadata = pipeline.GetChildByType("inputs")?.GetChildByType("input")?.GetChildByType("externalMetadataColumns");
             var columns = pipeline.GetChildByType("inputs")?.GetChildByType("input")?.GetChildByType("inputColumns");

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
                         $"component{pipeline.ID}.Columns.Add(\"{cv.Name}\");{Environment.NewLine}");

                     // Find the source column in our lineage data
                     var lineageId = column.Attributes["lineageId"];
                     var lo = pipeline.GetLineageObjectById(lineageId);

                     // Parameter setup instructions
                     if (lo == null)
                     {
                         _project.Log($"I couldn't find lineage column {lineageId}");
                         paramsetup.AppendFormat(
                             $"            // Unable to find column {lineageId}{Environment.NewLine}");
                     }
                     else
                     {
                         paramsetup.Append($"    dr[\"{cv.Name}\"] = {lo};{Environment.NewLine}");
                     }
                 }
             }

             colnames.Length -= 2;
             varnames.Length -= 2;
             TableParamCreate.Length -= 2;
             TableParamCreate.Append(")");

             // Insert the table parameter create statement in the project
             var sql_tableparam_resource = AddSqlResource(pipeline.GetParentDtsName() + "_WritePipe_TableParam",
                 TableParamCreate.ToString());

             // Produce a data set that we're going to process - name it after ourselves
             code.Add($"DataTable component{pipeline.ID} = new DataTable();");
             code.Add(TableSetup.ToString());

             // Check the inputs to see what component we're using as the source
             string? component = null;
             foreach (var incol in pipeline.GetChildByType("inputs")?.GetChildByType("input")?.GetChildByType("inputColumns")?
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
             code.Add();
             code.Add($"// Fill our table parameter in memory");
             code.Add($"for (int row = 0; row < {component}.Rows.Count; row++) {{");
             code.Add($"    DataRow dr = component{pipeline.ID}.NewRow();");
             code.Add(paramsetup.ToString());
             code.Add($"    component{pipeline.ID}.Rows.Add(dr);");
             code.Add($"}}");

             // Produce the SQL statement
             sql.AppendFormat("INSERT INTO {0} ({1}) SELECT {1} FROM @tableparam",
                 pipeline.GetChildByType("properties")?.GetChildByTypeAndAttr("property", "name", "OpenRowset")?.ContentValue,
                 colnames);
             var sql_resource_name = AddSqlResource(pipeline.GetParentDtsName() + "_WritePipe", sql.ToString());

             // Write the using clause for the connection
             code.Add();
             code.Add($"// Time to drop this all into the database");
             code.Add(
                 $"using (var conn = new {connprefix}Connection(ConfigurationManager.AppSettings[\"{connstr}\"]{fixup})) {{");
             code.Add($"    conn.Open();");

             // Ensure the table parameter type has been created correctly
             code.Add();
             code.Add($"    // Ensure the table parameter type has been created successfully");
             code.Add($"    CreateTableParamType(\"{sql_tableparam_resource}\", conn);");

             // Let's use our awesome table parameter style!
             code.Add();
             code.Add($"    // Insert all rows at once using fast table parameter insert");
             code.Add($"    using (var cmd = new {connprefix}Command(Resource1.{sql_resource_name}, conn)) {{");

             // Determine the timeout value specified in the pipeline
             var timeout_property =
                 pipeline.GetChildByType("properties")?.GetChildByTypeAndAttr("property", "name", "CommandTimeout");
             if (timeout_property != null)
             {
                 var timeout = int.Parse(timeout_property.ContentValue);
                 code.Add($"        cmd.CommandTimeout = {timeout};");
             }

             // Insert the table in one swoop
             code.Add(
                 $"        SqlParameter param = new SqlParameter(\"@tableparam\", SqlDbType.Structured);");
             code.Add($"        param.Value = component{pipeline.ID};");
             code.Add($"        param.TypeName = \"{tableparamname}\";");
             code.Add($"        cmd.Parameters.Add(param);");
             code.Add($"        cmd.ExecuteNonQuery();");
             code.Add($"    }}");
             code.Add($"}}");
             return code;
         }

         private CodeBlock EmitPipelineWriter(SsisObject pipeline)
         {
             var code = new CodeBlock();
             code.Add();
             code.Add($"// {pipeline.Name}");

             // Get the connection string GUID: it's connections.connection
             var conn_guid = pipeline.GetChildByType("connections")?.GetChildByType("connection")?
                 .Attributes["connectionManagerID"];
             var connstr = GetConnectionStringName(conn_guid);
             var connprefix = GetConnectionStringPrefix(conn_guid);

             // Report potential problems - can we programmatically convert an OleDb connection into an ADO.NET one?
             var fixup = "";
             if (connprefix == "OleDb")
             {
                 _project.Log("DESSIST had to rewrite an OleDb connection as an ADO.NET connection.  Please check it for correctness.");
                 connprefix = "Sql";
                 fixup = $".Replace(\"Provider=SQLNCLI10.1;\",\"\")";
             }

             // It's our problem to produce the SQL statement, because this writer uses calculated data!
             var sql = new StringBuilder();
             var colnames = new StringBuilder();
             var varnames = new StringBuilder();
             var paramsetup = new StringBuilder();

             // Retrieve the names of the columns we're inserting
             var metadata = pipeline.GetChildByType("inputs")?.GetChildByType("input")?.GetChildByType("externalMetadataColumns");
             var columns = pipeline.GetChildByType("inputs")?.GetChildByType("input")?.GetChildByType("inputColumns");

             // Okay, let's produce the columns we're inserting
             foreach (var column in columns?.Children ?? new List<SsisObject>())
             {
                 var mdcol = metadata?.GetChildByTypeAndAttr("externalMetadataColumn", "id",
                     column.Attributes["externalMetadataColumnId"]);

                 // List of columns in the insert
                 colnames.Append(mdcol?.Name);
                 colnames.Append(", ");

                 // List of parameter names in the values clause
                 varnames.Append("@");
                 varnames.Append(mdcol?.Name);
                 varnames.Append(", ");

                 // Find the source column in our lineage data
                 var lineageId = column.Attributes["lineageId"];
                 var lo = pipeline.GetLineageObjectById(lineageId);

                 // Parameter setup instructions
                 if (lo == null)
                 {
                     _project.Log($"I couldn't find lineage column {lineageId}");
                     paramsetup.Append($"            // Unable to find column {lineageId}{Environment.NewLine}");
                 }
                 else
                 {
                     // Is this a string?  If so, forcibly truncate it
                     if (mdcol?.Attributes["dataType"] == "str")
                     {
                         paramsetup.Append(
                             $"            cmd.Parameters.Add(new SqlParameter(\"@{mdcol.Name}\", SqlDbType.VarChar, {mdcol.Attributes["length"]}, ParameterDirection.Input, false, 0, 0, null, DataRowVersion.Current, {lo}));");
                     }
                     else if (mdcol?.Attributes["dataType"] == "wstr")
                     {
                         paramsetup.Append(
                             $"            cmd.Parameters.Add(new SqlParameter(\"@{mdcol.Name}\", SqlDbType.NVarChar, {mdcol.Attributes["length"]}, ParameterDirection.Input, false, 0, 0, null, DataRowVersion.Current, {lo}));");
                     }
                     else
                     {
                         paramsetup.Append($"            cmd.Parameters.AddWithValue(\"@{mdcol?.Name}\",{lo});");
                     }
                 }
             }

             colnames.Length -= 2;
             varnames.Length -= 2;

             // Produce the SQL statement
             sql.Append("INSERT INTO ");
             sql.Append(
                 pipeline.GetChildByType("properties")?.GetChildByTypeAndAttr("property", "name", "OpenRowset")?.ContentValue);
             sql.Append(" (");
             sql.Append(colnames);
             sql.Append(") VALUES (");
             sql.Append(varnames);
             sql.Append(")");
             var sql_resource_name = AddSqlResource(pipeline.GetParentDtsName() + "_WritePipe", sql.ToString());

             // Produce a data set that we're going to process - name it after ourselves
             code.Add($"DataTable component{pipeline.ID} = new DataTable();");

             // Write the using clause for the connection
             code.Add(
                 $"using (var conn = new {connprefix}Connection(ConfigurationManager.AppSettings[\"{connstr}\"]{fixup})) {{");
             code.Add($"    conn.Open();");

             // TODO: SQL Parameters should go in here

             // Check the inputs to see what component we're using as the source
             string? component = null;
             foreach (var incol in pipeline.GetChildByType("inputs")?.GetChildByType("input")?.GetChildByType("inputColumns")?
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
             code.Add($"    for (int row = 0; row < {component}.Rows.Count; row++) {{");
             code.Add($"        using (var cmd = new {connprefix}Command(Resource1.{sql_resource_name}, conn)) {{");
             code.Add($"            cmd.CommandTimeout = 0;");
             code.Add(paramsetup.ToString());
             code.Add($"            cmd.ExecuteNonQuery();");
             code.Add($"        }}");
             code.Add($"    }}");
             code.Add($"}}");
             return code;
         }

         private CodeBlock EmitPipelineTransform(SsisObject pipeline)
         {
             var code = new CodeBlock();
             
             // Add the columns we're generating
             var transforms = pipeline.GetChildByType("outputs")?.GetChildByTypeAndAttr("output", "isErrorOut", "false")?
                 .GetChildByType("outputColumns")?.Children;

             // Check the inputs to see what component we're using as the source
             var component = "component1";
             var inputcolumns = pipeline.GetChildByType("inputs")?.GetChildByType("input")?.GetChildByType("inputColumns");
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
                             _project.Log($"This SSIS pipeline is merging different component tables!");
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
                         if (property.Name == "SourceInputColumnLineageID")
                         {
                             var source_lineage = pipeline.GetLineageObjectById(property.ContentValue);
                             expression = $"Convert.ChangeType({source_lineage?.DataTableName}.Rows[row][\"{source_lineage?.FieldName}\"], typeof({cv.CsharpType()}));";
                         }
                         else if (property.Name == "FastParse")
                         {
                             // Don't need to do anything here
                         }
                         else if (property.Name == "Expression")
                         {
                             // Is this a lineage column?
                             expression = pipeline.FixExpression(cv.CsharpType(), pipeline._lineageColumns,
                                 property?.ContentValue, true);
                         }
                         else if (property.Name == "FriendlyExpression")
                         {
                             // This comment is useless - code.Add($"    // {1}", indent, property.ContentValue);
                         }
                         else
                         {
                             _project.Log($"I don't understand the output column property '{property.Name}'");
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

             return code;
         }

         private CodeBlock EmitFlatFilePipelineReader(SsisObject pipeline)
         {
             var code = new CodeBlock();
             UseCsvFile = true;

             // Produce a function header
             code.Add();
             code.Add($"// {pipeline.Name}");

             // Get the connection string GUID: it's connections.connection
             var conn_guid = pipeline.GetChildByType("connections")?.GetChildByType("connection")?
                 .Attributes["connectionManagerID"];
             var flat_file_obj = pipeline.Project.GetObjectByGuid(Guid.Parse(conn_guid)).Children[0].Children[0];

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
             var outputs = (pipeline.GetChildByType("outputs")?.GetChildByType("output")?.GetChildByType("outputColumns")?.Children ?? new List<SsisObject>())
                 .Where(child => child.DtsObjectType == "outputColumn").ToList();
             if (columns.Count != outputs.Count)
             {
                 pipeline.Project.Log(
                     "The list of columns in this flat file component doesn't match the columns in the data source.");
             }

             // Now pair up all the outputs to generate header columns and lineage objects
             var headers = new StringBuilder("new string[] {");
             for (var i = 0; i < columns.Count; i++)
             {
                 var name = columns[i].DtsObjectName;
                 headers.Append($"\"{name.EscapeDoubleQuotes()}\", ");
                 var lo = new LineageObject(outputs[i].Attributes["lineageId"], $"component{pipeline.ID}", name);
                 pipeline._lineageColumns.Add(lo);
             }

             headers.Length -= 2;
             headers.Append(" }");

             // This is set to -1 if the column names aren't in the first row in the data file
             var qual = flat_file_obj.Properties["TextQualifier"].FixDelimiter();
             var delim = columns[0].Properties["ColumnDelimiter"].FixDelimiter();
             if (flat_file_obj.Properties["ColumnNamesInFirstDataRow"] == "-1")
             {
                 code.Add(
                     $"DataTable component{pipeline.ID} = CSVFile.CSV.LoadDataTable(\"{filename.EscapeDoubleQuotes()}\", {headers}, true, '{delim}', '{qual}');");
             }
             else
             {
                 code.Add(
                     $"DataTable component{pipeline.ID} = CSVFile.CSV.LoadDataTable(\"{filename.EscapeDoubleQuotes()}\", true, true, '{delim}', '{qual}');");
             }
             return code;
         }

         private CodeBlock EmitPipelineReader(SsisObject pipeline)
         {
             var code = new CodeBlock();
             
             code.Add($"// {pipeline.Name}");

             // Get the connection string GUID: it's connections.connection
             var conn_guid = pipeline.GetChildByType("connections")?.GetChildByType("connection")?
                 .Attributes["connectionManagerID"];
             var connstr = GetConnectionStringName(conn_guid);
             var connprefix = GetConnectionStringPrefix(conn_guid);

             // Get the SQL statement
             var sql = pipeline.GetChildByType("properties")?.GetChildByTypeAndAttr("property", "name", "SqlCommand")?.ContentValue;
             if (sql == null)
             {
                 var rowset = pipeline.GetChildByType("properties")?.GetChildByTypeAndAttr("property", "name", "OpenRowset")?
                     .ContentValue;
                 if (rowset == null)
                 {
                     sql = "COULD NOT FIND SQL STATEMENT";
                     _project.Log($"Could not find SQL for {pipeline.Name} in {pipeline.DtsId}");
                 }
                 else
                 {
                     sql = "SELECT * FROM " + rowset;
                 }
             }

             var sql_resource_name = AddSqlResource(pipeline.GetParentDtsName() + "_ReadPipe", sql);

             // Produce a data set that we're going to process - name it after ourselves
             code.Add($"DataTable component{pipeline.ID} = new DataTable();");

             // Keep track of the lineage of all of our output columns 
             // TODO: Handle error output columns
             foreach (var outcol in pipeline.GetChildByType("outputs")?.GetChildByTypeAndAttr("output", "isErrorOut", "false")?
                          .GetChildByType("outputColumns")?.Children ?? new List<SsisObject>())
             {
                 var lo = new LineageObject(outcol.Attributes["lineageId"], "component" + pipeline.ID,
                     outcol.Name);
                 pipeline._lineageColumns.Add(lo);
             }

             // Write the using clause for the connection
             code.Add(
                 $"using (var conn = new {connprefix}Connection(ConfigurationManager.AppSettings[\"{connstr}\"])) {{");
             code.Add($"    conn.Open();");
             code.Add($"    using (var cmd = new {connprefix}Command(Resource1.{sql_resource_name}, conn)) {{");

             // Determine the timeout value specified in the pipeline
             var timeout_property =
                 pipeline.GetChildByType("properties")?.GetChildByTypeAndAttr("property", "name", "CommandTimeout");
             if (timeout_property != null)
             {
                 var timeout = int.Parse(timeout_property.ContentValue ?? "0");
                 code.Add($"        cmd.CommandTimeout = {timeout};");
             }

             // Okay, let's load the parameters
             var paramlist = pipeline.GetChildByType("properties")?.GetChildByTypeAndAttr("property", "name", "ParameterMapping");
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
                             code.Add($"        cmd.Parameters.Add(new OleDbParameter(\"@p{paramnum}\",{v?.DtsObjectName}));");
                         }
                         else
                         {
                             code.Add($"        cmd.Parameters.AddWithValue(\"@{parts[0]}\",{v?.DtsObjectName});");
                         }
                     }

                     paramnum++;
                 }
             }

             // Finish up the pipeline reader
             code.Add($"        {connprefix}DataReader dr = cmd.ExecuteReader();");
             code.Add($"        component{pipeline.ID}.Load(dr);");
             code.Add($"        dr.Close();");
             code.Add($"    }}");
             code.Add($"}}");
             return code;
         }
        
         /// <summary>
         /// Produce this variable to the current stream
         /// </summary>
         /// <param name="obj"></param>
         /// <param name="as_global"></param>
         /// <returns></returns>
         internal CodeBlock EmitVariable(SsisObject obj, bool as_global)
         {
             var code = new CodeBlock(); 
             var vd = new ProgramVariable(obj, as_global);

             // Do we add comments for these variables?
             var privilege = "";
             if (as_global)
             {
                 if (!string.IsNullOrEmpty(vd.Comment))
                 {
                     code.Add($"/// <summary>");
                     code.Add($"/// {vd.Comment}");
                     code.Add($"/// </summary>");
                 }

                 privilege = "public static ";
             }

             // Write it out
             if (string.IsNullOrEmpty(vd.DefaultValue))
             {
                 code.Add($"{privilege}{vd.CSharpType} {vd.VariableName};");
             }
             else
             {
                 code.Add($"{privilege}{vd.CSharpType} {vd.VariableName} = {vd.DefaultValue};");
             }

             // Keep track of variables so we can do type conversions in the future!
             _project.RegisterVariable(vd.VariableName, vd);
             return code;
         }
    }
}
