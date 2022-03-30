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

namespace csharp_dessist
{
    public class ProjectWriter
    {
        public static string AppFolder;

        public static List<string> ProjectFiles = new List<string>();
        public static List<string> AllFiles = new List<string>();
        public static List<string> DllFiles = new List<string>();

        public static bool UseSqlServerManagementObjects = false;
        public static bool UseCsvFile = false;

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
            var sb = new StringBuilder();
            if (string.IsNullOrEmpty(name)) {
                sb.Append("UnnamedStatement");
            } else {
                foreach (char c in name) {
                    if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) {
                        sb.Append(c);
                    } else {
                        sb.Append("_");
                    }
                }
            }

            // Uniqueify!
            var newname = sb.ToString().ToLower();
            var i = 1;
            while (_resources.ContainsKey(newname)) {
                newname = sb.ToString().ToLower() + "_" + i.ToString();
                i++;
            }
            return newname;
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
            Trace.Log("Decompilation completed.");
            if (SourceWriter._help_messages.Count > 0) {
                var helpfile = Path.Combine(folder, "ImportErrors.txt");
                File.Delete(helpfile);
                using (var sw = new StreamWriter(helpfile)) {
                    foreach (var help in SourceWriter._help_messages) {
                        sw.WriteLine(help);
                    }
                }
                Trace.Log($"{SourceWriter._help_messages.Count} import errors encountered.");
                if (SourceWriter._help_messages.Count > 0)
                {
                    Trace.Log($"Import errors written to {helpfile}");
                    Trace.Log();
                    Trace.Log("Please consider opening an issue on GitHub to report these import errors to  the DESSIST team.");
                    Trace.Log("Visit our website online here:");
                    Trace.Log("    https://github.com/tspence/csharp-dessist");
                }
            }
        }
    }
}
