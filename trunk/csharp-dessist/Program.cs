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
using System.Xml;
using System.IO;
using System.Text.RegularExpressions;

namespace csharp_dessist
{
    public enum SqlCompatibilityType { SQL2008, SQL2005 };

    public class Program
    {
        public static SqlCompatibilityType gSqlMode = SqlCompatibilityType.SQL2008;

        /// <summary>
        /// Use the friendly console library for a command line interface
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            CommandWrapLib.ConsoleWrapper(null, "Program", "ParseSsisPackage", args);
        }

        /// <summary>
        /// Attempt to read an SSIS package and produce a meaningful C# program
        /// </summary>
        /// <param name="ssis_filename"></param>
        /// <param name="output_folder"></param>
        public static void ParseSsisPackage(string ssis_filename, string output_folder, SqlCompatibilityType SqlMode = SqlCompatibilityType.SQL2008)
        {
            XmlReaderSettings set = new XmlReaderSettings();
            set.IgnoreWhitespace = true;
            SsisObject o = new SsisObject();
            gSqlMode = SqlMode;

            // TODO: Should read the dtproj file instead of the dtsx file, then produce multiple classes, one for each .DTSX file

            // Read in the file, one element at a time
            XmlDocument xd = new XmlDocument();
            xd.Load(ssis_filename);
            ReadObject(xd.DocumentElement, o);

            // Now let's produce something meaningful out of this mess!
            ProduceSsisDotNetPackage(Path.GetFileNameWithoutExtension(ssis_filename), o, output_folder);
        }

        #region Write the SSIS package to a C# folder
        /// <summary>
        /// Produce C# files that replicate the functionality of an SSIS package
        /// </summary>
        /// <param name="o"></param>
        private static void ProduceSsisDotNetPackage(string projectname, SsisObject o, string output_folder)
        {
            ProjectWriter.AppFolder = output_folder;

            // First find all the connection strings and write them to an app.config file
            var connstrings = from SsisObject c in o.Children where c.DtsObjectType == "DTS:ConnectionManager" select c;
            ConnectionWriter.WriteAppConfig(connstrings, Path.Combine(output_folder, "app.config"));

            // Next, write all the executable functions to the main file
            var functions = from SsisObject c in o.Children where c.DtsObjectType == "DTS:Executable" select c;
            var variables = from SsisObject c in o.Children where c.DtsObjectType == "DTS:Variable" select c;
            WriteProgram(variables, functions, Path.Combine(output_folder, "program.cs"), projectname);

            // Next write the resources and the project file
            ProjectWriter.WriteResourceAndProjectFile(output_folder, projectname);
        }

        /// <summary>
        /// Write a program file that has all the major executable instructions as functions
        /// </summary>
        /// <param name="variables"></param>
        /// <param name="functions"></param>
        /// <param name="p"></param>
        private static void WriteProgram(IEnumerable<SsisObject> variables, IEnumerable<SsisObject> functions, string filename, string appname)
        {
            using (SourceWriter.SourceFileStream = new StreamWriter(filename, false, Encoding.UTF8)) {

                // Write the header
                SourceWriter.Write(@"using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Xml;
using System.IO;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace ");
                SourceWriter.Write(appname);
                SourceWriter.Write(@"
{
    public static class Extensions
    {
        public static string FixupOleDb(this string s)
        {
            int p = s.IndexOf(""Provider="", StringComparison.CurrentCultureIgnoreCase);
            if (p >= 0) {
                int p2 = s.IndexOf(';', p + 1);
                return s.Substring(0, p) + s.Substring(p2 + 1);
            }
            return s;
        }
    }

    public class Program
    {
        public static RecursiveTimeLog timer = new RecursiveTimeLog();
");

                // Support for table params
                if (gSqlMode == SqlCompatibilityType.SQL2008) {
                SourceWriter.Write(@"
        public static List<string> CreatedTableParams = new List<string>();
        public static bool MustCreateTableParamFor(string s)
        {
            bool result = !CreatedTableParams.Contains(s);
            if (result) {
                CreatedTableParams.Add(s);
            }
            return result;
        }
");
                }

                SourceWriter.Write(@"

#region Main()
        /// <summary>
        /// Main Function
        /// </summary>
        /// <param name=""args""></param>
        static void Main(string[] args)
        {
            ");

                // Emit a function call to the first function in the application
                SourceWriter.Write(functions.FirstOrDefault().GetFunctionName());
                SourceWriter.Write(@"();
            Console.WriteLine(timer.GetTimings());
        }
#endregion


#region Global Variables
        /// <summary>
        /// Global Variables
        /// </summary>
");

                // Write each variable out as if it's a global
                foreach (SsisObject v in variables) {
                    v.EmitVariable("        ", true);
                }

                SourceWriter.Write(@"
#endregion


#region SSIS Extracted Functions
");
                // Write each executable out as a function
                foreach (SsisObject v in functions) {
                    v.EmitFunction("        ", new List<ProgramVariable>());
                }

                // Write the footer
                SourceWriter.WriteLine(@"
#endregion
    }
}");
            }
        }
        #endregion

        #region Read in an SSIS DTSX file
        /// <summary>
        /// Recursive read function
        /// </summary>
        /// <param name="xr"></param>
        /// <param name="o"></param>
        private static void ReadObject(XmlElement el, SsisObject o)
        {
            // Read in the object name
            o.DtsObjectType = el.Name;

            // Read in attributes
            foreach (XmlAttribute xa in el.Attributes) {
                o.Attributes.Add(xa.Name, xa.Value);
            }

            // Iterate through all children of this element
            foreach (XmlNode child in el.ChildNodes) {

                // For child elements
                if (child is XmlElement) {
                    XmlElement child_el = child as XmlElement;

                    // Read in a DTS Property
                    if (child.Name == "DTS:Property" || child.Name == "DTS:PropertyExpression") {
                        ReadDtsProperty(child_el, o);

                        // Everything else is a sub-object
                    } else {
                        SsisObject child_obj = new SsisObject();
                        ReadObject(child_el, child_obj);
                        child_obj.Parent = o;
                        o.Children.Add(child_obj);
                    }
                } else if (child is XmlText) {
                    o.ContentValue = child.InnerText;
                } else if (child is XmlCDataSection) {
                    o.ContentValue = child.InnerText;
                } else {
                    Console.WriteLine("Help");
                }
            }
        }

        /// <summary>
        /// Read in a DTS property from the XML stream
        /// </summary>
        /// <param name="xr"></param>
        /// <param name="o"></param>
        private static void ReadDtsProperty(XmlElement el, SsisObject o)
        {
            string prop_name = null;

            // Read all the attributes
            foreach (XmlAttribute xa in el.Attributes) {
                if (String.Equals(xa.Name, "DTS:Name", StringComparison.CurrentCultureIgnoreCase)) {
                    prop_name = xa.Value;
                    break;
                }
            }

            // Set the property
            o.SetProperty(prop_name, el.InnerText);
        }
        #endregion
    }
}
