using System.Text;
using System.Text.RegularExpressions;

namespace Dessist
{
    public static class StringUtilities
    {
        /// <summary>
        /// Fixup a project name using underscores
        /// </summary>
        /// <param name="projectProjectName"></param>
        /// <returns></returns>
        public static string CleanNamespaceName(this string projectProjectName)
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
        /// Take a hex-encoded delimiter and turn it into a real string
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static string FixDelimiter(this string s)
        {
            // Here's a regex
            var r = new Regex("_x[0-9A-F][0-9A-F][0-9A-F][0-9A-F]_");

            // Chip apart into individual hex bits
            while (true) {

                // Look for a match
                var m = r.Match(s);
                if (m.Success) {
                    var val = Convert.ToInt32(m.Value.Substring(2,4), 16);
                    var c = (char)val;
                    s = s.Substring(0, m.Index) + c + s.Substring(m.Index + m.Length);
                } else {
                    break;
                }
            }

            // Here's what you've got
            return s;
        }
        
        /// <summary>
        /// Converts the namespace into something usable by C#
        /// </summary>
        /// <param name="original_variable_name"></param>
        /// <returns></returns>
        public static string FixVariableName(this string original_variable_name)
        {
            // We are simply stripping out namespaces for the moment
            var p = original_variable_name.IndexOf("::", StringComparison.Ordinal);
            return p > 0 ? original_variable_name.Substring(p + 2) : original_variable_name;
        }

        /// <summary>
        /// Escape double quotes into backslash-doublequotes
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static string EscapeDoubleQuotes(this string s)
        {
            return s.Replace("\"", "\\\"");
        }

        /// <summary>
        /// Fixup a name so that it is unique within a list
        /// </summary>
        /// <param name="name"></param>
        /// <param name="list"></param>
        /// <returns></returns>
        public static string Uniqueify(string name, List<string> list)
        {
            var cleanedName = name.CleanNamespaceName();
            var i = 0;
            var newName = cleanedName;
            while (list.Contains(newName))
            {
                i++;
                newName = $"{cleanedName}_{i}";
            }
            list.Add(newName);
            return newName;
        }
        
        /// <summary>
        /// Determines if the SQL statement is written in SQLCMD style (e.g. including "GO" statements) or as a regular SQL string
        /// </summary>
        /// <param name="sql"></param>
        /// <returns></returns>
        public static bool IsSql(string sql)
        {
            // TODO: There really should be a better way to determine this
            return sql.Contains("\nGO");
        }
    }
}