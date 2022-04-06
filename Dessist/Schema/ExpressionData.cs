/*
 * 2012-2015 Ted Spence, http://tedspence.com
 * License: http://www.apache.org/licenses/LICENSE-2.0 
 * Home page: https://github.com/tspence/csharp-dessist
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Dessist
{
    public class ExpressionData
    {
        protected string _expression;
        public List<LineageObject> Lineage;
        protected List<Token> _tokens;
        protected string _expected_type;

        // Some regular expression searches for tokens - I'm not sure if they're really the optimal solution but they work
        private static Regex STRING_REGEX = new Regex("^[\"](?<value>.*)[\"]");
        private static Regex NUMBER_REGEX = new Regex("^(?<value>\\d*)");
        private readonly SsisProject _project;

        public ExpressionData(SsisProject project, string expected_type, List<LineageObject> lineage, string raw_expression)
        {
            _project = project;
            _expected_type = expected_type;
            _expression = raw_expression;
            Lineage = lineage;
            _tokens = new List<Token>();
            string s = raw_expression;

            // Iterate through the string
            while (!String.IsNullOrEmpty(s)) {
                Token t = ConsumeToken(ref s);
                if (t != null) _tokens.Add(t);
            }
        }

        public Token? ConsumeToken(ref string s)
        {
            // Get the first character
            var c = s[0];

            // Is it whitespace?  Just consume this character and move on
            if (c == ' ' || c == '\r' || c == '\n' || c == '\t') {
                s = s.Substring(1);
                return null;

            // Is this a lineage column reference?
            } else if (c == '#') {
                return new LineageToken(_project, ref s, this);

            // Is this a global variable reference?
            } else if (c == '@') {
                return new VariableToken(_project, ref s, this);

            // Is this a type conversion?  If so, we need one more token
            } else if (c == '(') {
                return new ConversionToken(_project, ref s, this);

            // Is this a math operation?
            } else if (c == '+' || c == '-' || c == '*' || c == '/') {
                s = s.Substring(1); 
                return (new OperationToken() { Op = c.ToString() });

            // Check for solo negation or not-equals
            } else if (c == '!') {
                if (s[1] == '=') {
                    s = s.Substring(2);
                    return (new OperationToken() { Op = "!=" });
                }
                s = s.Substring(1);
                return (new OperationToken() { Op = "!" });

            // This could be either assignment or equality
            } else if (c == '=') {
                if (s[1] == '=') {
                    s = s.Substring(2); 
                    return (new OperationToken() { Op = "==" });
                }
                s = s.Substring(1);
                return (new OperationToken() { Op = "=" });

            // Is this a string constant?
            } else if (c == '"') {
                var m = STRING_REGEX.Match(s);
                if (m.Success) {
                    s = s.Substring(m.Index + m.Length);
                    return (new ConstantToken() { Value = m.Captures[0].Value, TypeRef = "System.String" });
                }

            // Is this a numeric constant?
            } else if (c >= '0' && c <= '9') {
                var m = NUMBER_REGEX.Match(s);
                if (m.Success) {
                    s = s.Substring(m.Index + m.Length);
                    return (new ConstantToken() { Value = m.Captures[0].Value, TypeRef = "System.Int32" });
                }

            // Is this boolean true or false?
            } else if (s.StartsWith("true", StringComparison.CurrentCultureIgnoreCase)) {
                s = s.Substring(4);
                return (new ConstantToken() { Value = "true", TypeRef = "System.Boolean" });
            } else if (s.StartsWith("false", StringComparison.CurrentCultureIgnoreCase)) {
                s = s.Substring(5);
                return (new ConstantToken() { Value = "false", TypeRef = "System.Boolean" });
            }

            // Did we fail to process this token?  Fail out
            _project.Log($"Unable to parse expression '{_expression}'");
            s = "";
            return null;
        }

        public string ToCSharp(bool inline)
        {
            var sb = new StringBuilder();
            foreach (var t in _tokens) {

                // Is this an unassigned lineage token object?  We store those in DataTables, which are "object"s.  
                // If so, produce a strict type expectation
                if ((t is LineageToken) && (_expected_type != "System.String")) {
                    sb.AppendFormat("(({0})({1}))", _expected_type, t.ToCSharp());
                } else {
                    sb.Append(t.ToCSharp());
                }
            }

            // Only show comment and end-statement if this is a solo statement
            if (!inline) {
                sb.Append("; // Raw: ");
                sb.Append(_expression);
            }
            return sb.ToString();
        }
    }
}
