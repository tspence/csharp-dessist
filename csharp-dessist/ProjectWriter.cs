/*
 * 2012 Ted Spence, http://tedspence.com
 * License: http://www.apache.org/licenses/LICENSE-2.0 
 * Home page: https://code.google.com/p/csharp-command-line-wrapper
 * 
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

        public static void EmitScriptProject(SsisObject o, string indent)
        {
            // Find the script object child
            var script = o.GetChildByType("DTS:ObjectData").GetChildByType("ScriptProject");

            // Create a folder for this script
            string project_folder = Path.Combine(AppFolder, o.GetFolderName());
            Directory.CreateDirectory(project_folder);

            // Extract all the individual script files in this script
            foreach (SsisObject child in script.Children) {
                string fn = project_folder + child.Attributes["Name"];
                string dir = Path.GetDirectoryName(fn);
                Directory.CreateDirectory(dir);

                if (child.DtsObjectType == "BinaryItem") {
                    byte[] contents = System.Convert.FromBase64String(child.ContentValue);
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
            string munged_name = FixResourceName(name);
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
            StringBuilder sb = new StringBuilder();
            if (String.IsNullOrEmpty(name)) {
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
            string newname = sb.ToString().ToLower();
            int i = 1;
            while (_resources.ContainsKey(newname)) {
                newname = sb.ToString().ToLower() + "_" + i.ToString();
                i++;
            }
            return newname;
        }

        public static void WriteResourceAndProjectFile(string folder, string appname)
        {
            // Ensure resources folder exists
            string resfolder = Path.Combine(folder, "Resources");
            Directory.CreateDirectory(resfolder);
            string propfolder = Path.Combine(folder, "Properties");
            Directory.CreateDirectory(propfolder);

            // Let's produce the template of the resources
            StringBuilder resfile = new StringBuilder();
            StringBuilder prjfile = new StringBuilder();
            StringBuilder desfile = new StringBuilder();
            foreach (KeyValuePair<string, string> kvp in _resources) {
                resfile.Append(Resource1.IndividualResourceSnippet.Replace("@@RESNAME@@", kvp.Key));
                prjfile.Append(Resource1.IndividualResourceProjectSnippet.Replace("@@RESNAME@@", kvp.Key));
                desfile.Append(Resource1.IndividualResourceDesignerTemplate.Replace("@@RESNAME@@", kvp.Key));

                // Write this to a file as well
                File.WriteAllText(Path.Combine(resfolder, kvp.Key + ".sql"), kvp.Value);
            }

            // Iterate through DLLS
            StringBuilder DllReferences = new StringBuilder();
            foreach (string dll in DllFiles) {
                DllReferences.Append(
                    Resource1.DllReferenceTemplate
                    .Replace("@@FILENAMEWITHOUTEXTENSION@@", Path.GetFileNameWithoutExtension(dll))
                    .Replace("@@RELATIVEPATH@@", dll.Substring(folder.Length+1)));
            }

            // Spit out the resource file
            File.WriteAllText(Path.Combine(folder, "Resource1.resx"), Resource1.ResourceTemplate.Replace("@@RESOURCES@@", resfile.ToString()));

            // Resource needs a designer file too!
            string designer =
                Resource1.ResourceDesignerTemplate
                .Replace("@@APPNAME@@", appname)
                .Replace("@@RESOURCES@@", desfile.ToString());
            File.WriteAllText(Path.Combine(folder, "Resource1.Designer.cs"), designer);

            // Spit out the project file
            Guid proj_guid = Guid.NewGuid();
            string project =
                Resource1.ProjectTemplate
                .Replace("@@RESOURCES@@", prjfile.ToString())
                .Replace("@@DLLS@@", DllReferences.ToString())
                .Replace("@@APPNAME@@", appname)
                .Replace("@@PROJGUID@@", proj_guid.ToString().ToUpper());
            File.WriteAllText(Path.Combine(folder, appname + ".csproj"), project);

            // Copy the Microsoft SQL Server objects!
            File.Copy("Microsoft.SqlServer.ConnectionInfo.dll", Path.Combine(folder, "Microsoft.SqlServer.ConnectionInfo.dll"), true);
            File.Copy("Microsoft.SqlServer.Management.Sdk.Sfc.dll", Path.Combine(folder, "Microsoft.SqlServer.Management.Sdk.Sfc.dll"), true);
            File.Copy("Microsoft.SqlServer.Smo.dll", Path.Combine(folder, "Microsoft.SqlServer.Smo.dll"), true);

            // Spit out the solution file
            Guid sln_guid = Guid.NewGuid();
            string solution =
                Resource1.SolutionTemplate
                .Replace("@@PROJGUID@@", proj_guid.ToString().ToUpper())
                .Replace("@@SOLUTIONGUID@@", sln_guid.ToString().ToUpper())
                .Replace("@@APPNAME@@", appname);
            File.WriteAllText(Path.Combine(folder, appname + ".sln"), solution);

            // Spit out the assembly file
            Guid asy_guid = Guid.NewGuid();
            string assembly =
                Resource1.AssemblyTemplate
                .Replace("@@ASSEMBLYGUID@@", asy_guid.ToString())
                .Replace("@@APPNAME@@", appname);
            File.WriteAllText(Path.Combine(propfolder, "AssemblyInfo.cs"), assembly);

            // Spit out the assembly file
            File.WriteAllText(Path.Combine(propfolder, "RecursiveTimeLog.cs"), Resource1.RecursiveTimeLog);

            // Write out the help notes
            Console.WriteLine("Decompilation completed.");
            if (SourceWriter._help_messages.Count > 0) {
                string helpfile = Path.Combine(folder, "ImportErrors.txt");
                File.Delete(helpfile);
                using (StreamWriter sw = new StreamWriter(helpfile)) {
                    foreach (String help in SourceWriter._help_messages) {
                        sw.WriteLine(help);
                    }
                }
                Console.WriteLine("{0} import errors encountered.", SourceWriter._help_messages.Count);
                Console.WriteLine("Import errors written to " + helpfile);
                Console.WriteLine();
                Console.WriteLine("Please consider sharing a copy of your SSIS package with the DESSIST team.");
                Console.WriteLine("Visit our website online here:");
                Console.WriteLine("    https://code.google.com/p/csharp-dessist/");
            }
        }
    }
}
