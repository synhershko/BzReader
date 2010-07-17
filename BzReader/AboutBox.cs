using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;

namespace BzReader
{
    partial class AboutBox : Form
    {
        public AboutBox()
        {
            InitializeComponent();

            //  Initialize the AboutBox to display the product information from the assembly information.
            //  Change assembly information settings for your application through either:
            //  - Project->Properties->Application->Assembly Information
            //  - AssemblyInfo.cs
            this.Text = String.Format(Properties.Resources.AboutProduct, BzReader.Properties.Resources.BzReader);
            this.labelProductName.Text = BzReader.Properties.Resources.BzReader;
            this.labelVersion.Text = String.Format(Properties.Resources.AboutVersion, AssemblyVersion);
            this.labelCopyright.Text = BzReader.Properties.Resources.ProductLicense;
            this.textBoxDescription.Text = BzReader.Properties.Resources.ProductDescription;
        }

        public string AssemblyVersion
        {
            get
            {
                return Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
        }

        private void okButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
