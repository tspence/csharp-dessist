using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

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

        /// <summary>
        /// Save the content value of a complex object
        /// </summary>
        public string ContentValue;


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
        /// Produce this variable to the current stream
        /// </summary>
        /// <param name="indent_depth"></param>
        /// <param name="as_global"></param>
        /// <param name="sw"></param>
        internal void EmitVariable(int indent_depth, bool as_global, StreamWriter sw)
        {
            string indent = new String(' ', indent_depth);

            if (as_global) {
                if (!String.IsNullOrEmpty(Description)) {
                    sw.WriteLine();
                    sw.WriteLine("{0}/// <summary>", indent);
                    sw.WriteLine("{0}/// {1}", indent, Description);
                    sw.WriteLine("{0}/// </summary>", indent);
                }
                sw.WriteLine(String.Format(@"{0}public string {1} = ""{2}"";", indent, DtsObjectName, ContentValue));
            } else {
                sw.WriteLine(String.Format(@"{0}string {1} = ""{2}"";", indent, DtsObjectName, ContentValue));
            }
        }

        /// <summary>
        /// Produce this variable to the current stream
        /// </summary>
        /// <param name="indent_depth"></param>
        /// <param name="as_global"></param>
        /// <param name="sw"></param>
        internal void EmitFunction(int indent_depth, StreamWriter sw)
        {
            string indent = new String(' ', indent_depth);

            // Header and comments
            sw.WriteLine();
            if (!String.IsNullOrEmpty(Description)) {
                sw.WriteLine("{0}/// <summary>", indent);
                sw.WriteLine("{0}/// {1}", indent, Description);
                sw.WriteLine("{0}/// </summary>", indent);
            }

            // Function intro
            sw.WriteLine(String.Format("{0}public static void {1}()\n        {{\n", indent, FixFunctionName(DtsObjectName)));

            // TODO: Is there an exception handler?  How many types of event handlers are there?
            // TODO: Check precedence constraints
            // TODO: Ignore ObjectData elements; they are worthless wrapper elements
            // TODO: Create a general purpose lookup of DTSID objects

            // Function body
            foreach (SsisObject o in Children) {

                // For variables, emit them within this function
                if (o.DtsObjectType == "DTS:Variable") {
                    EmitVariable(indent_depth + 4, false, sw);
                } else if (o.DtsObjectType == "DTS:Executable") {
                    EmitFunctionCall(o, indent_depth + 4, sw);
                } else {
                    Console.WriteLine("Help!");
                }
            }

            // End of function
            sw.WriteLine("{0}}}", indent);

            // Now emit any other functions that are chained into this
            foreach (SsisObject o in Children) {
                if (o.DtsObjectType == "DTS:Executable") {
                    o.EmitFunction(indent_depth, sw);
                }
            }
        }

        /// <summary>
        /// Write out a function call
        /// </summary>
        /// <param name="indent"></param>
        /// <param name="sw"></param>
        private void EmitFunctionCall(SsisObject executable_to_call, int indent_depth, StreamWriter sw)
        {
            string indent = new String(' ', indent_depth);

            sw.WriteLine(String.Format(@"{0}{1}();", indent, FixFunctionName(executable_to_call.DtsObjectName)));
        }

        private static string FixFunctionName(string str)
        {
            Regex rgx = new Regex("[^a-zA-Z0-9]");
            return rgx.Replace(str, "_");
        }
    }
}
