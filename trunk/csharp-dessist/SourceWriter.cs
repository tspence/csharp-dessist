using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace csharp_dessist
{
    public class SourceWriter
    {
        public static StreamWriter SourceFileStream = null;

        public static int LineNumber = 1;

        public static List<string> _help_messages = new List<string>();

        public static void Help(SsisObject obj, string message)
        {
            // Figure out what to display
            string s = null;
            if (obj == null) {
                s = "Help! " + message;
            } else {
                Guid g = obj.GetNearestGuid();
                if (g == Guid.Empty) {
                    s = String.Format("File {0} Line {1}: {2}", "program.cs", LineNumber, message);
                } else {
                    s = String.Format("File {0} Line {1}: {2} (DTSID: {3})", "program.cs", LineNumber, message, obj.GetNearestGuid());
                }
            }
            WriteLine("// ImportError: {0}", message);

            // Log this problem
            _help_messages.Add(s);

            // Emit a comment
            Console.WriteLine(s);
        }

        public static void Write(string s, params object[] arg)
        {
            string newstring = null;
            if (arg == null || arg.Length == 0) {
                newstring = s;
            } else {
                newstring = String.Format(s, arg);
            }
            SourceFileStream.Write(newstring);

            // Also count how many embedded newlines there were in that string!
            LineNumber += newstring.Count(c => c == '\n');
        }

        /// <summary>
        /// Write a line of text to the sourcecode file, optionally with parameters
        /// </summary>
        /// <param name="s"></param>
        /// <param name="?"></param>
        public static void WriteLine(string s, params object[] arg)
        {
            Write(s + Environment.NewLine, arg);
        }

        /// <summary>
        /// Write a blank line of text to the sourcecode file
        /// </summary>
        /// <param name="s"></param>
        /// <param name="?"></param>
        public static void WriteLine()
        {
            Write(Environment.NewLine);
        }
    }
}
