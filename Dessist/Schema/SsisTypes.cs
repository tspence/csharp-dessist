// ReSharper disable StringLiteralTypo
namespace Dessist
{
    public class SsisTypes
    {
        /// <summary>
        /// Lookup a C# type name
        /// </summary>
        /// <param name="project"></param>
        /// <param name="variableTypeName"></param>
        /// <returns></returns>
        public static string? LookupSsisTypeName(SsisProject project, string variableTypeName)
        {
            if (variableTypeName.StartsWith("DT_")) variableTypeName = variableTypeName.Substring(3);
            variableTypeName = variableTypeName.ToLower();
            switch (variableTypeName)
            {
                case "i2":
                    return "System.Int16";
                case "i4":
                    return "System.Int32";
                case "i8":
                    return "System.Int64";
                case "str":
                case "wstr":
                    return "System.String";
                case "dbtimestamp":
                    return "System.DateTime";
                case "r4":
                case "r8":
                    return "double";
                case "cy":
                case "numeric":
                    return "System.Decimal";
                default:
                    project.Log($"I don't yet understand the SSIS type named {variableTypeName}");
                    break;
            }

            return null;
        }

        /// <summary>
        /// Lookup an SQL type referenced in an SSIS project
        /// </summary>
        /// <param name="project"></param>
        /// <param name="ssisTypeName"></param>
        /// <param name="length"></param>
        /// <param name="precision"></param>
        /// <param name="scale"></param>
        /// <returns></returns>
        public static string? LookupSsisTypeSqlName(SsisProject project, string ssisTypeName, string? length, int precision,
            int scale)
        {
            // Skip Data Transformation Underscore
            if (ssisTypeName.StartsWith("DT_")) ssisTypeName = ssisTypeName.Substring(3);
            ssisTypeName = ssisTypeName.ToLower();

            switch (ssisTypeName)
            {
                case "i2":
                    return "smallint";
                case "i4":
                    return "int";
                case "i8":
                    return "bigint";
                case "str":
                    return $"varchar({length ?? "max"})";
                case "wstr":
                    return $"nvarchar({length ?? "max"})";
                case "dbtimestamp":
                    return "datetime";
                case "r4":
                    return "real";
                case "r8":
                    return "float";
                case "cy":
                    return "money";
                case "numeric":
                    return $"decimal({precision},{scale})";
                default:
                    project.Log("I don't yet understand the SSIS type named {ssistype}");
                    break;
            }

            return null;
        }
    }
}