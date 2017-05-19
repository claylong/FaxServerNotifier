using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Fax_Server_Notifier
{
    public partial class SettingsDialog : Form
    {
        FaxServerSystray notifier = FaxServerSystray.GetNotifierObject();
        public SettingsDialog()
        {
            InitializeComponent();
        }

        private void applySettings()
        {
            notifier.faxServerURI = this.txtServerURI.Text;
            notifier.saveSettings();
            notifier.connectToFaxServer();
        }

        private void buttonApply_Click(object sender, EventArgs e)
        {
            applySettings();
        }

        private void SettingsDialog_Load(object sender, EventArgs e)
        {
            this.txtServerURI.Text = notifier.faxServerURI;
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {
            applySettings();
            this.Close();
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }


    }
}