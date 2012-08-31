using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace csharp_dessist
{
    public class HelpWriter
    {
        public static List<string> _help_messages = new List<string>();

        public static void Help(SsisObject obj, string message)
        {
            string s = null;
            if (obj == null) {
                s = "Help! " + message;
            } else {
                s = String.Format("Help!  Problem in {0}: {1}", obj.GetNearestGuid(), message);
            }
            _help_messages.Add(s);
            //Console.WriteLine(s);
        }
    }
}
