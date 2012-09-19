using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace csharp_dessist
{
    #region Token types
    public class Token
    {
        public string TypeRef;
        public virtual string ToCSharp()
        {
            return "";
        }
    }

    public class VariableToken : Token
    {
        public SsisObject VariableRef;
        public string Namespace;
        public string Variable;

        private static Regex VARIABLE_REGEX = new Regex("^(?<one>[a-zA-Z0-9]+)");
        private static Regex NAMESPACE_VARIABLE_REGEX = new Regex("^[@][[](?<one>[a-zA-Z0-9]+)::(?<two>[a-zA-Z0-9]+)]");

        public VariableToken(ref string s, ExpressionData parent)
        {
            Match m = VARIABLE_REGEX.Match(s.Substring(1));
            if (m.Success) {
                s = s.Substring(m.Index + m.Length + 1);
                Variable = m.Captures[0].Value;
                Namespace = "";
            } else {
                m = new Regex("\\[(?<namespace>\\w+)::(?<variable>\\w+)\\]").Match(s.Substring(1));
                if (m.Success) {
                    Variable = m.Groups[2].Value;
                    Namespace = m.Groups[1].Value;
                    s = s.Substring(m.Index + m.Length + 1);
                } else {
                    HelpWriter.Help(null, "Unable to parse variable '" + s + "'");
                }
            }
        }

        public override string ToCSharp()
        {
            return Variable;
        }
    }

    public class LineageToken : Token
    {
        public LineageObject LineageRef;

        private static Regex LINEAGE_REGEX = new Regex("^[#](?<col>\\d*)");

        public LineageToken(ref string s, ExpressionData parent)
        {
            Match m = LINEAGE_REGEX.Match(s);
            if (m.Success) {
                var li = (from LineageObject l in parent.Lineage where l.LineageId == m.Groups[1].Value select l).FirstOrDefault();
                if (li != null) {
                    s = s.Substring(m.Index + m.Length);
                    LineageRef = li;
                } else {
                    HelpWriter.Help(null, "Unable to find lineage reference #" + s);
                }
            }
        }

        public override string ToCSharp()
        {
            return LineageRef.ToString();
        }
    }

    public class ConversionToken : Token
    {
        public int ScaleRef;
        public Token TokenToConvert;

        private static Regex CONVERT_REGEX = new Regex("^\\((?<type>\\w+),(?<scale>\\d+)\\)");

        public ConversionToken(ref string s, ExpressionData parent)
        {
            Match m = CONVERT_REGEX.Match(s);
            if (m.Success) {
                s = s.Substring(m.Index + m.Length);
                TypeRef = SsisObject.LookupSsisTypeName(m.Groups[1].Value);
                TokenToConvert = parent.ConsumeToken(ref s);
            } else {
                HelpWriter.Help(null, "Unable to parse conversion token " + s);
            }
        }

        public override string ToCSharp()
        {
            return String.Format("Convert.ChangeType({0}, typeof({1}))", TokenToConvert.ToCSharp(), TypeRef);
        }
    }

    public class ConstantToken : Token
    {
        public string Value;

        public override string ToCSharp()
        {
            //if (TypeRef == "System.String") {
            //    return "\"" + Value + "\"";
            //} else {
                return Value;
            //}
        }
    }

    public class OperationToken : Token
    {
        public string Op;

        public override string ToCSharp()
        {
            return " " + Op + " ";
        }
    }
    #endregion

    public class ExpressionData
    {
        protected string _expression;
        public List<LineageObject> Lineage;
        protected List<Token> _tokens;

        // Some regular expression searches for tokens - I'm not sure if they're really the optimal solution but they work
        private static Regex STRING_REGEX = new Regex("^[\"](?<value>.*)[\"]");
        private static Regex NUMBER_REGEX = new Regex("^(?<value>\\d*)");
        
        public ExpressionData(List<LineageObject> lineage, string raw_expression)
        {
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

        public Token ConsumeToken(ref string s)
        {
            // Get the first character
            char c = s[0];

            // Is it whitespace?  Just consume this character and move on
            if (c == ' ' || c == '\r' || c == '\n' || c == '\t') {
                s = s.Substring(1);
                return null;

            // Is this a lineage column reference?
            } else if (c == '#') {
                return new LineageToken(ref s, this);

            // Is this a global variable reference?
            } else if (c == '@') {
                return new VariableToken(ref s, this);

            // Is this a type conversion?  If so, we need one more token
            } else if (c == '(') {
                return new ConversionToken(ref s, this);

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
                Match m = STRING_REGEX.Match(s);
                if (m.Success) {
                    s = s.Substring(m.Index + m.Length);
                    return (new ConstantToken() { Value = m.Captures[0].Value, TypeRef = "System.String" });
                }

            // Is this a numeric constant?
            } else if (c >= '0' && c <= '9') {
                Match m = NUMBER_REGEX.Match(s);
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
            HelpWriter.Help(null, "Unable to parse expression '" + _expression + "'");
            s = "";
            return null;
        }

        public string ToCSharp(bool inline)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Token t in _tokens) {
                sb.Append(t.ToCSharp());
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
