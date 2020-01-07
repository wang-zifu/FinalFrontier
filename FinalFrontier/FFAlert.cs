﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FinalFrontier
{
    public partial class FFAlert : Form
    {
        public FFAlert(Analyzer ana)
        {
            InitializeComponent();
            alertHeadline.Text = "Email könnte schadhaft sein!!!";
            alertContent.Text = ana.alertContent;
            this.Refresh();
        }
    }
}