using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace csharp_dessist
{
    public class VariableData
    {
        public string DtsType;
        public string CSharpType;
        public string DefaultValue;
        public string Comment;
        public string VariableName;
        public string Namespace;
        public bool IsGlobal;

        public VariableData(SsisObject o, bool as_global)
        {
            // Figure out what type of a variable we are
            DtsType = o.GetChildByType("DTS:VariableValue").Attributes["DTS:DataType"];
            CSharpType = null;
            DefaultValue = o.GetChildByType("DTS:VariableValue").ContentValue;
            Comment = o.Description;
            Namespace = o.Properties["Namespace"];
            IsGlobal = as_global;
            VariableName = o.DtsObjectName;

            // Here are the DTS type codes I know
            if (DtsType == "3") {
                CSharpType = "int";
            } else if (DtsType == "8") {
                CSharpType = "string";
                if (!String.IsNullOrEmpty(DefaultValue)) {
                    DefaultValue = "\"" + DefaultValue + "\"";
                }
            } else if (DtsType == "13") {
                CSharpType = "DataTable";
                DefaultValue = "new DataTable()";
            } else if (DtsType == "2") {
                CSharpType = "short";
            } else if (DtsType == "11") {
                CSharpType = "bool";
                if (DefaultValue == "1") {
                    DefaultValue = "true";
                } else {
                    DefaultValue = "false";
                }
            } else if (DtsType == "20") {
                CSharpType = "long";
            } else if (DtsType == "7") {
                CSharpType = "DateTime";
                if (!String.IsNullOrEmpty(DefaultValue)) {
                    DefaultValue = "DateTime.Parse(\"" + DefaultValue + "\")";
                }
            } else {
                SourceWriter.Help(o, "I don't understand DTS type " + DtsType);
            }
        }
    }
}
