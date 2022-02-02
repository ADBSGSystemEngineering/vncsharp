using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace WindowsFormsApplication1
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            this.remoteDesktop1.VncAddress = "";
            this.remoteDesktop1.VncPassword = "";
            //this.remoteDesktop1.vncConnectTimeout = 5;
            //this.remoteDesktop1.VncConnect = true;
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            this.remoteDesktop1.VncAddress = "172.17.5.21";
            this.remoteDesktop1.VncPassword = "ADB";
            //this.remoteDesktop1.vncConnectTimeout = 5;
            this.remoteDesktop1.VncConnect=true;
        }

        private void btnDisconnect_Click(object sender, EventArgs e)
        {
            this.remoteDesktop1.VncConnect = false;
        }

        private void btnNoPassword_Click(object sender, EventArgs e)
        {
            this.remoteDesktop1.VncPassword = "";
            remoteDesktop1.VncConnect = true;
        }
    }
}
