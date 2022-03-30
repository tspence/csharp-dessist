/*
 * 2012-2015 Ted Spence, http://tedspence.com
 * License: http://www.apache.org/licenses/LICENSE-2.0 
 * Home page: https://github.com/tspence/csharp-dessist
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace csharp_dessist
{

    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var form = new MainForm();
            Application.EnableVisualStyles();
            Application.Run(form);
        }
    }
}
