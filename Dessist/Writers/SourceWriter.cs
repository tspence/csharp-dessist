﻿/*
 * 2012-2015 Ted Spence, http://tedspence.com
 * License: http://www.apache.org/licenses/LICENSE-2.0 
 * Home page: https://github.com/tspence/csharp-dessist
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
// ReSharper disable StringLiteralTypo

namespace Dessist
{
    public class SourceWriter
    {
        public static StreamWriter SourceFileStream = null;

        public static int LineNumber = 1;
        public static int IndentSize = 0;

        public static List<string> _help_messages = new List<string>();
        private readonly SsisProject _project;

        public SourceWriter(SsisProject project)
        {
            _project = project;
        }

        public void Help(SsisObject? obj, string message)
        {
            // Figure out what to display
            string? s = null;
            if (obj == null) {
                s = "Help! " + message;
            } else {
                var g = obj.GetNearestGuid();
                if (g == Guid.Empty) {
                    s = $"File \"program.cs\" Line {LineNumber}: {message}";
                } else {
                    s = $"File \"program.cs\" Line {LineNumber}: {message} (DTSID: {obj.GetNearestGuid()})";
                }
            }
            WriteLine("{0}// ImportError: {1}", new string(' ', IndentSize), message);

            // Log this problem
            _help_messages.Add(s);

            // Emit a comment
            _project.Log(s);
        }

        public void Write(string s, params object[]? arg)
        {
            string? newstring = null;
            if (arg == null || arg.Length == 0) {
                newstring = s;
            } else {
                newstring = String.Format(s, arg);
            }
            SourceFileStream.Write(newstring);

            // Also count how many embedded newlines there were in that string!
            LineNumber += newstring.Count(c => c == '\n');

            // Remember the current indent size!
            IndentSize = 0;
            while (newstring[IndentSize] == ' ') IndentSize++;
        }

        /// <summary>
        /// Write a line of text to the sourcecode file, optionally with parameters
        /// </summary>
        /// <param name="s"></param>
        /// <param name="?"></param>
        public void WriteLine(string s, params object[] arg)
        {
            Write(s + Environment.NewLine, arg);
        }
    }
}
