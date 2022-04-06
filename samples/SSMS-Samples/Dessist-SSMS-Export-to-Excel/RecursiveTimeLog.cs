using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SSMS-Export-to-Excel
{
    public class RecursiveTimeLog
    {
        public class StackTiming
        {
            public string FunctionName;
            public TimeSpan CumulativeTimeSpent;
            public DateTime EntryTime = DateTime.Now;

            public void ClockOut()
            {
                TimeSpan ts = DateTime.Now - EntryTime;
                CumulativeTimeSpent += ts;
            }

            public void ClockIn()
            {
                EntryTime = DateTime.Now;
            }
        }

        public class FunctionData
        {
            public TimeSpan CumulativeTimeSpent;
            public int TimesCalled;
        }

        protected Dictionary<string, FunctionData> _time_dict = new Dictionary<string, FunctionData>();
        protected Stack<StackTiming> _func_stack = new Stack<StackTiming>();

        /// <summary>
        /// Begin an entry on the stack trace
        /// </summary>
        /// <param name="func"></param>
        public void Enter(string func)
        {
            // "Clock out" the prior item on the stack
            if (_func_stack.Count > 0) {
                _func_stack.Peek().ClockOut();
            }

            // Create a new stack frame and push our data onto the stack
            StackTiming obj = new StackTiming() { FunctionName = func };
            obj.ClockIn();
            _func_stack.Push(obj);
        }

        /// <summary>
        /// Pop an entry on the stack trace
        /// </summary>
        public void Leave()
        {
            // Remove the top item from the stack, record its time to the global log, and retire it
            StackTiming obj = _func_stack.Pop();
            obj.ClockOut();
            FunctionData fd = null;
            if (_time_dict.TryGetValue(obj.FunctionName, out fd)) {
                fd.CumulativeTimeSpent += obj.CumulativeTimeSpent;
            } else {
                fd = new FunctionData();
                fd.CumulativeTimeSpent = obj.CumulativeTimeSpent;
            }
            fd.TimesCalled += 1;
            _time_dict[obj.FunctionName] = fd;

            // Resume timing the next item
            if (_func_stack.Count > 0) {
                _func_stack.Peek().ClockIn();
            }
        }

        /// <summary>
        /// Report all the timings that occurred
        /// </summary>
        /// <returns></returns>
        public string GetTimings()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Function\tTime\tCalls\r\n");
            foreach (KeyValuePair<string, FunctionData> kvp in _time_dict) {
                sb.AppendFormat("{0}\t{1}\t{2}\r\n", kvp.Key, kvp.Value.CumulativeTimeSpent, kvp.Value.TimesCalled);
            }
            return sb.ToString();
        }
    }
}
