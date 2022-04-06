using System.Text;

namespace Dessist.Writers
{

    public class CodeBlock
    {
        public List<string> Lines = new List<string>();
        
        public void Add(string line = "")
        {
            Lines.Add(line);    
        }

        public string ToString(int indent)
        {
            var sb = new StringBuilder();
            var indentString = "".PadRight(indent);
            foreach (var line in Lines)
            {
                sb.Append(indentString);
                sb.Append(line);
            }

            return sb.ToString();
        }

        public void Merge(CodeBlock otherBlock, int indent)
        {
            var indentString = "".PadRight(indent);
            foreach (var line in otherBlock.Lines)
            {
                Lines.Add($"{indentString}{line}");
            }
        }
    }
}