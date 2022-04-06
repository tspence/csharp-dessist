/*
 * 2012-2015 Ted Spence, http://tedspence.com
 * License: http://www.apache.org/licenses/LICENSE-2.0 
 * Home page: https://github.com/tspence/csharp-dessist
 */
using System.Text;

namespace Dessist
{
    public class ProjectWriter
    {
        public static string AppFolder;

        public static List<string> ProjectFiles = new List<string>();
        public static List<string> AllFiles = new List<string>();
        public static List<string> DllFiles = new List<string>();

        public static bool UseSqlServerManagementObjects = false;
        public static bool UseCsvFile = false;

        /// <summary>
        /// Produce C# files that replicate the functionality of an SSIS package
        /// </summary>
        /// <param name="sqlMode"></param>
        /// <param name="output_folder"></param>
        public void ProduceSsisDotNetPackage(SsisProject project, SqlCompatibilityType sqlMode, string output_folder)
        {
            // Make sure output folder exists
            Directory.CreateDirectory(output_folder);
            ProjectWriter.AppFolder = output_folder;

            // First find all the connection strings and write them to an app.config file
            var connectionStrings = from SsisObject c in project.RootObject.Children where c.DtsObjectType == "DTS:ConnectionManager" select c;
            ConnectionWriter.WriteAppConfig(connectionStrings, Path.Combine(output_folder, "app.config"));

            // Next, write all the executable functions to the main file
            var functions = (from SsisObject c in project.RootObject.Children where c.DtsObjectType == "DTS:Executable" select c).ToList();
            if (!functions.Any())
            {
                var executables = from SsisObject c in project.RootObject.Children where c.DtsObjectType == "DTS:Executables" select c;
                var functionList = new List<SsisObject>();
                foreach (var exec in executables)
                {
                    functionList.AddRange(from e in exec.Children where e.DtsObjectType == "DTS:Executable" select e);
                }

                if (functionList.Count == 0)
                {
                    project.Log("No functions ('DTS:Executable') objects found in the specified file.");
                    return;
                }

                functions = functionList;
            }

            var variables = from SsisObject c in project.RootObject.Children where c.DtsObjectType == "DTS:Variable" select c;
            WriteProgram(sqlMode, variables, functions, Path.Combine(output_folder, "program.cs"), project.Name);

            // Next write the resources and the project file
            ProjectWriter.WriteResourceAndProjectFile(output_folder, project.Name);
        }
        
        /// <summary>
        /// Write a program file that has all the major executable instructions as functions
        /// </summary>
        /// <param name="sqlMode"></param>
        /// <param name="variables"></param>
        /// <param name="functions"></param>
        /// <param name="filename"></param>
        /// <param name="appname"></param>
        private static void WriteProgram(SqlCompatibilityType sqlMode, IEnumerable<SsisObject> variables,
            List<SsisObject> functions, string filename, string appname)
        {
            using (SourceWriter.SourceFileStream = new StreamWriter(filename, false, Encoding.UTF8))
            {
                var smo_using = "";
                var tableparamstatic = "";

                // Are we using SMO mode?
                if (ProjectWriter.UseSqlServerManagementObjects)
                {
                    smo_using = Resource1.SqlSmoUsingTemplate;
                }

                // Are we using SQL 2008 mode?
                if (sqlMode == SqlCompatibilityType.SQL2008)
                {
                    tableparamstatic = Resource1.TableParameterStaticTemplate;
                }

                // Write the header in one fell swoop
                SourceWriter.Write(
                    Resource1.ProgramHeaderTemplate
                        .Replace("@@USINGSQLSMO@@", smo_using)
                        .Replace("@@NAMESPACE@@", appname)
                        .Replace("@@TABLEPARAMSTATIC@@", tableparamstatic)
                        .Replace("@@MAINFUNCTION@@", functions?.FirstOrDefault()?.GetFunctionName() ?? "UnknownMainFunction")
                );

                // Write each variable out as if it's a global
                foreach (var v in variables)
                {
                    v.EmitVariable("        ", true);
                }

                SourceWriter.WriteLine();
                SourceWriter.WriteLine();

                // Write each executable out as a function
                foreach (var v in functions)
                {
                    v.EmitFunction(sqlMode, "        ", new List<ProgramVariable>());
                }

                SourceWriter.WriteLine(Resource1.ProgramFooterTemplate);
            }
        }
        
        public static void EmitScriptProject(SsisObject o, string indent)
        {
            // Find the script object child
            var script = o.GetChildByType("DTS:ObjectData").GetChildByType("ScriptProject");

            // Create a folder for this script
            var project_folder = Path.Combine(AppFolder, o.GetFolderName());
            Directory.CreateDirectory(project_folder);

            // Extract all the individual script files in this script
            foreach (var child in script.Children) {
                var fn = project_folder + child.Attributes["Name"];
                var dir = Path.GetDirectoryName(fn);
                Directory.CreateDirectory(dir);

                if (child.DtsObjectType == "BinaryItem") {
                    var contents = System.Convert.FromBase64String(child.ContentValue);
                    File.WriteAllBytes(fn, contents);
                } else if (child.DtsObjectType == "ProjectItem") {
                    File.WriteAllText(fn, child.ContentValue);
                }

                // Handle DLL files specially - they are binary!  Oh yeah base64 encoded
                if (fn.EndsWith(".dll")) {
                    DllFiles.Add(fn);

                    // Note this as a potential problem
                    SourceWriter.Help(o, "The Visual Basic project " + child.Attributes["Name"] + " was embedded in the DTSX project.  Visual Basic code cannot be automatically converted.");

                    // Show the user that this is how the script should be executed, if they want to fix it
                    SourceWriter.WriteLine(@"{0}//{1}.ScriptMain sm = new {1}.ScriptMain();", indent, Path.GetFileNameWithoutExtension(fn).Replace("scripttask", "ScriptTask"));
                    SourceWriter.WriteLine(@"{0}//sm.Main();", indent);

                    // Is this a project file?
                } else if (fn.EndsWith(".vbproj") || fn.EndsWith(".csproj")) {
                    ProjectFiles.Add(fn);
                } else {
                    AllFiles.Add(fn);
                }
            }
        }

        private static Dictionary<string, string> _resources = new Dictionary<string, string>();

        public static string AddSqlResource(string name, string resource)
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
        private static string FixResourceName(string name)
        {
            var newName = (name ?? "UnnamedStatement").CleanNamespaceName();
            return StringUtilities.Uniqueify(newName, _resources);
        }

        public static void WriteResourceAndProjectFile(string folder, string appname)
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
    }
}
