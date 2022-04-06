using System.Text.RegularExpressions;

namespace Dessist
{

    public class Token
    {
        private readonly SsisProject _project;

        public virtual string ToCSharp()
        {
            return "(ABSTRACT TOKEN)";
        }
    }

    public class VariableToken : Token
    {
        public SsisObject VariableRef;
        public string Namespace;
        public string Variable;

        private static Regex VARIABLE_REGEX = new Regex("^(?<one>[a-zA-Z0-9]+)");
        private static Regex NAMESPACE_VARIABLE_REGEX = new Regex("^[@][[](?<one>[a-zA-Z0-9]+)::(?<two>[a-zA-Z0-9]+)]");

        public VariableToken(SsisProject project, ref string s, ExpressionData parent)
        {
            Match m = VARIABLE_REGEX.Match(s.Substring(1));
            if (m.Success)
            {
                s = s.Substring(m.Index + m.Length + 1);
                Variable = m.Captures[0].Value;
                Namespace = "";
            }
            else
            {
                m = new Regex("\\[(?<namespace>\\w+)::(?<variable>\\w+)\\]").Match(s.Substring(1));
                if (m.Success)
                {
                    Variable = m.Groups[2].Value;
                    Namespace = m.Groups[1].Value;
                    s = s.Substring(m.Index + m.Length + 1);
                }
                else
                {
                    project.Log($"Unable to parse variable '{s}'");
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

        public LineageToken(SsisProject project, ref string s, ExpressionData parent)
        {
            var m = LINEAGE_REGEX.Match(s);
            if (m.Success)
            {
                var li = (from LineageObject l in parent.Lineage where l.LineageId == m.Groups[1].Value select l)
                    .FirstOrDefault();
                if (li != null)
                {
                    s = s.Substring(m.Index + m.Length);
                    LineageRef = li;
                }
                else
                {
                    project.Log($"Unable to find lineage reference #{s}");
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
        public string? TypeRef;
        public Token TokenToConvert;

        private static Regex CONVERT_REGEX = new Regex("^\\((?<type>\\w+),(?<scale>\\d+)\\)");

        public ConversionToken(SsisProject project, ref string s, ExpressionData parent)
        {
            Match m = CONVERT_REGEX.Match(s);
            if (m.Success)
            {
                s = s.Substring(m.Index + m.Length);
                TypeRef = SsisTypes.LookupSsisTypeName(project, m.Groups[1].Value);
                TokenToConvert = parent.ConsumeToken(ref s);
            }
            else
            {
                project.Log($"Unable to parse conversion token {s}");
            }
        }

        public ConversionToken(string ParamTypeRef, Token ParamTokenToConvert)
        {
            TypeRef = ParamTypeRef;
            TokenToConvert = ParamTokenToConvert;
        }

        public override string ToCSharp()
        {
            return TypeRef == "System.String" ? $"({TokenToConvert.ToCSharp()}).ToString()" : $"({TypeRef})(Convert.ChangeType({TokenToConvert.ToCSharp()}, typeof({TypeRef})))";
        }
    }

    public class ConstantToken : Token
    {
        public string TypeRef;
        public string Value;

        public override string ToCSharp()
        {
            return $"({TypeRef}){Value}";
        }
    }

    public class OperationToken : Token
    {
        public string Op;

        public override string ToCSharp()
        {
            return $" {Op} ";
        }
    }
}