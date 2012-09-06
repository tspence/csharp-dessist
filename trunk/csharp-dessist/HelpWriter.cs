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
            // Figure out what to display
            string s = null;
            if (obj == null) {
                s = "Help! " + message;
            } else {
                Guid g = obj.GetNearestGuid();
                if (g == Guid.Empty) {
                    s = String.Format("Help!  Problem in {0}: {1}", obj.GetNearestGuid(), message);
                } else {
                    s = String.Format("Help!  Problem in {0}: {1}", obj.GetNearestGuid(), message);
                }
            }

            // Log this problem
            _help_messages.Add(s);

            // Emit a comment
            //Console.WriteLine(s);
        }
    }
}
