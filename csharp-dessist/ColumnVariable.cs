/*
 * 2012-2015 Ted Spence, http://tedspence.com
 * License: http://www.apache.org/licenses/LICENSE-2.0 
 * Home page: https://github.com/tspence/csharp-dessist
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace csharp_dessist
{
    public class ColumnVariable
    {
        public int ID;
        public string Name;
        public string SsisTypeName;
        public string LineageID;
        public string Length;
        public string Precision;
        public string Scale;
        public string CodePage;

        public ColumnVariable(int id, string name, string ssis_type_name, string lineage_id, string precision, string scale, string codepage, string length)
        {
            ID = id;
            Name = name;
            SsisTypeName = ssis_type_name;
            LineageID = lineage_id;
            Precision = precision;
            Scale = scale;
            CodePage = codepage;
            Length = length;
        }

        public ColumnVariable(SsisObject o)
        {
            int.TryParse(o.Attributes["id"], out ID);
            Name = o.Attributes["name"];
            SsisTypeName = o.Attributes["dataType"];
            if (o.Attributes.ContainsKey("lineageId")) LineageID = o.Attributes["lineageId"];
            Precision = o.Attributes["precision"];
            Scale = o.Attributes["scale"];
            CodePage = o.Attributes["codePage"];
            Length = o.Attributes["length"];
        }

        public string CsharpType()
        {
            return LookupSsisTypeName(SsisTypeName);
        }

        public string SqlDbType()
        {
            return LookupSsisTypeSqlName(SsisTypeName, Length, int.Parse(Precision), int.Parse(Scale));
        }

        public static string LookupSsisTypeName(string p)
        {
            // Skip Data Transformation Underscore
            if (p.StartsWith("DT_")) p = p.Substring(3);
            p = p.ToLower();

            // Okay, let's check real stuff
            if (p == "i2") {
                return "System.Int16";
            } else if (p == "i4") {
                return "System.Int32";
            } else if (p == "i8") {
                return "System.Int64";
            } else if (p == "str" || p == "wstr") {
                return "System.String";
            } else if (p == "dbtimestamp") {
                return "System.DateTime";
            } else if (p == "r4" || p == "r8") {
                return "double";

                // Currency & numerics both become decimals
            } else if (p == "cy" || p == "numeric") {
                return "System.Decimal";
            } else {
                SourceWriter.Help(null, "I don't yet understand the SSIS type named " + p);
            }
            return null;
        }

        public static string LookupSsisTypeSqlName(string ssistype, string length, int precision, int scale)
        {
            // Skip Data Transformation Underscore
            if (ssistype.StartsWith("DT_")) ssistype = ssistype.Substring(3);
            ssistype = ssistype.ToLower();

            // Okay, let's check real stuff
            if (ssistype == "i2") {
                return "smallint";
            } else if (ssistype == "i4") {
                return "int";
            } else if (ssistype == "i8") {
                return "bigint";
            } else if (ssistype == "str") {
                return String.Format("varchar({0})", length == null ? "max" : length);
            } else if (ssistype == "wstr") {
                return String.Format("nvarchar({0})", length == null ? "max" : length);
            } else if (ssistype == "dbtimestamp") {
                return "datetime";
            } else if (ssistype == "r4") {
                return "real";
            } else if (ssistype == "r8") {
                return "float";

                // Currency
            } else if (ssistype == "cy") {
                return "money";
            } else if (ssistype == "numeric") {
                return String.Format("decimal({0},{1})", precision, scale);
            } else {
                SourceWriter.Help(null, "I don't yet understand the SSIS type named " + ssistype);
            }
            return null;
        }

    }
}
