using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace csharp_dessist
{
    public class ResourceWriter
    {
        private static Dictionary<string, string> _resources = new Dictionary<string,string>();

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
                .Replace("@@APPNAME@@", appname)
                .Replace("@@PROJGUID@@", proj_guid.ToString().ToUpper());
            File.WriteAllText(Path.Combine(folder, appname + ".csproj"), project);

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
        }
    }
}
