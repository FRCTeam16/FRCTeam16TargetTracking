using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace FRCTeam16TargetTracking
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        public void SetDebugInfo(string txt)
        {
            if (!chkPauseUpdate.Checked)
            {
                richTextBox1.Text = txt;
            }
        }

        public void SetTargetInfo(string txt)
        {
            if (!chkPauseUpdate.Checked)
            {
                richTextBox2.Text = txt;
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Hide();
        }

        private void chkPauseUpdate_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }
    }
}
