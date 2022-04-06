using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Dessist;

// ReSharper disable SuggestBaseTypeForParameter
// ReSharper disable LocalizableElement
// ReSharper disable StringLiteralTypo

namespace Dessist
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        private void btnSelectFile_Click(object sender, EventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.CheckFileExists = true;
            ofd.Multiselect = false;
            ofd.Filter = "DTSX files (*.dtsx)|*.dtsx|All Files (*.*)|*.*||";
            var result = ofd.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtSsisFilename.Text = ofd.FileName;
                if (string.IsNullOrWhiteSpace(txtOutputFolder.Text))
                {
                    txtOutputFolder.Text = Path.Combine(Path.GetDirectoryName(ofd.FileName), $"Dessist-{Path.GetFileNameWithoutExtension(ofd.FileName)}");
                }
            }
        }

        private void btnSelectFolder_Click(object sender, EventArgs e)
        {
            var fbd = new FolderBrowserDialog();
            fbd.ShowNewFolderButton = true;
            var result = fbd.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtOutputFolder.Text = fbd.SelectedPath;
            }
        }

        private void btnRun_Click(object sender, EventArgs e)
        {
            if (!File.Exists(txtSsisFilename.Text))
            {
                MessageBox.Show($"Cannot find file {txtSsisFilename.Text}");
                return;
            }
            var ssis = SsisProject.LoadFromFile(txtSsisFilename.Text);
            ssis.ProduceSsisDotNetPackage(SqlCompatibilityType.SQL2008, txtOutputFolder.Text);
            
            // Show the log, and launch the folder
            txtProcessingLogs.Text = ssis.GetLog();
            Process.Start(txtOutputFolder.Text);
        }

   }
}