using System.Diagnostics;
using System.Text;
using System.Xml;
using csharp_dessist;

namespace Dessist
{
    public class SsisProject
    {
        private StringBuilder _logs = new StringBuilder();
        private SsisObject _ssis = null;
        private string _projectName = null;
        private string _namespaceName = null;
        
        /// <summary>
        /// Log something
        /// </summary>
        /// <param name="message"></param>
        public void Log(string message = "")
        {
            _logs.AppendLine(message);
        }

        /// <summary>
        /// Retrieve logs from this Ssis Project
        /// </summary>
        /// <returns></returns>
        public string GetLog()
        {
            return _logs.ToString();
        }
        
        /// <summary>
        /// Attempt to read an SSIS package into memory
        /// </summary>
        /// <param name="ssis_filename"></param>
        public static SsisProject LoadFromFile(string ssis_filename)
        {
            var project = new SsisProject();
            project._ssis = new SsisObject();
            project._projectName = Path.GetFileNameWithoutExtension(ssis_filename);
            project._namespaceName = CleanNamespaceName(project._projectName);

            // Read in the file, one element at a time
            // TODO: Should read the dtproj file instead of the dtsx file, then produce multiple classes, one for each .DTSX file
            var xd = new XmlDocument();
            xd.Load(ssis_filename);
            ReadObject(xd.DocumentElement, project._ssis);
            return project;
        }

        private static string CleanNamespaceName(string projectProjectName)
        {
            var sb = new StringBuilder();
            foreach (var c in projectProjectName)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Produce C# files that replicate the functionality of an SSIS package
        /// </summary>
        /// <param name="sqlMode"></param>
        /// <param name="output_folder"></param>
        public void ProduceSsisDotNetPackage(SqlCompatibilityType sqlMode, string output_folder)
        {
            // Make sure output folder exists
            Directory.CreateDirectory(output_folder);
            ProjectWriter.AppFolder = output_folder;

            // First find all the connection strings and write them to an app.config file
            var connectionStrings = from SsisObject c in _ssis.Children where c.DtsObjectType == "DTS:ConnectionManager" select c;
            ConnectionWriter.WriteAppConfig(connectionStrings, Path.Combine(output_folder, "app.config"));

            // Next, write all the executable functions to the main file
            var functions = (from SsisObject c in _ssis.Children where c.DtsObjectType == "DTS:Executable" select c).ToList();
            if (!functions.Any())
            {
                var executables = from SsisObject c in _ssis.Children where c.DtsObjectType == "DTS:Executables" select c;
                var functionList = new List<SsisObject>();
                foreach (var exec in executables)
                {
                    functionList.AddRange(from e in exec.Children where e.DtsObjectType == "DTS:Executable" select e);
                }

                if (functionList.Count == 0)
                {
                    Log("No functions ('DTS:Executable') objects found in the specified file.");
                    return;
                }

                functions = functionList;
            }

            var variables = from SsisObject c in _ssis.Children where c.DtsObjectType == "DTS:Variable" select c;
            WriteProgram(sqlMode, variables, functions, Path.Combine(output_folder, "program.cs"), _projectName);

            // Next write the resources and the project file
            ProjectWriter.WriteResourceAndProjectFile(output_folder, _projectName);
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

        /// <summary>
        /// Recursive read function
        /// </summary>
        /// <param name="el"></param>
        /// <param name="o"></param>
        private static void ReadObject(XmlElement el, SsisObject o)
        {
            // Read in the object name
            o.DtsObjectType = el.Name;

            // Read in attributes
            foreach (XmlAttribute xa in el.Attributes)
            {
                o.Attributes.Add(xa.Name, xa.Value);
            }

            // Iterate through all children of this element
            foreach (XmlNode child in el.ChildNodes)
            {
                switch (child)
                {
                    // For child elements
                    case XmlElement element:
                    {
                        var child_el = element;

                        // Read in a DTS Property
                        if (element.Name == "DTS:Property" || element.Name == "DTS:PropertyExpression")
                        {
                            ReadDtsProperty(child_el, o);
                        }
                        else
                        {
                            var child_obj = new SsisObject();
                            ReadObject(child_el, child_obj);
                            child_obj.Parent = o;
                            o.Children.Add(child_obj);
                        }

                        break;
                    }
                    case XmlText _:
                    case XmlCDataSection _:
                        o.ContentValue = child.InnerText;
                        break;
                    default:
                        Trace.Log("Help");
                        break;
                }
            }
        }

        /// <summary>
        /// Read in a DTS property from the XML stream
        /// </summary>
        /// <param name="el"></param>
        /// <param name="o"></param>
        private static void ReadDtsProperty(XmlElement el, SsisObject o)
        {
            var prop_name =
                (from XmlAttribute xa in el.Attributes
                    where string.Equals(xa.Name, "DTS:Name", StringComparison.CurrentCultureIgnoreCase)
                    select xa.Value).FirstOrDefault();

            // Set the property
            o.SetProperty(prop_name, el.InnerText);
        }
    }
}