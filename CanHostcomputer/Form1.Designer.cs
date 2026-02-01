using System.Windows.Forms;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace CanHostcomputer
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            button1 = new Button();
            textBox1 = new TextBox();
            button2 = new Button();
            RxtextBox1 = new TextBox();
            button3 = new Button();
            comboBoxAdapter = new ComboBox();
            comboBoxBaud = new ComboBox();
            SuspendLayout();
            // 
            // button1
            // 
            button1.Location = new Point(262, 39);
            button1.Margin = new Padding(2);
            button1.Name = "button1";
            button1.Size = new Size(92, 28);
            button1.TabIndex = 0;
            button1.Text = "打开can";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // textBox1
            // 
            textBox1.Location = new Point(10, 82);
            textBox1.Margin = new Padding(2);
            textBox1.Multiline = true;
            textBox1.Name = "textBox1";
            textBox1.ReadOnly = true;
            textBox1.Size = new Size(448, 395);
            textBox1.TabIndex = 1;
            // 
            // button2
            // 
            button2.Location = new Point(551, 34);
            button2.Margin = new Padding(2);
            button2.Name = "button2";
            button2.Size = new Size(92, 28);
            button2.TabIndex = 2;
            button2.Text = "发送报文";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
            // 
            // RxtextBox1
            // 
            RxtextBox1.Location = new Point(523, 82);
            RxtextBox1.Margin = new Padding(2);
            RxtextBox1.Multiline = true;
            RxtextBox1.Name = "RxtextBox1";
            RxtextBox1.ReadOnly = true;
            RxtextBox1.Size = new Size(448, 395);
            RxtextBox1.TabIndex = 3;
            // 
            // button3
            // 
            button3.Location = new Point(375, 39);
            button3.Margin = new Padding(2);
            button3.Name = "button3";
            button3.Size = new Size(92, 28);
            button3.TabIndex = 0;
            button3.Text = "关闭can";
            button3.UseVisualStyleBackColor = true;
            button3.Click += button3_Click;
            // 
            // comboBoxAdapter
            // 
            comboBoxAdapter.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxAdapter.Items.AddRange(new object[] { "Kvaser", "Loopback" });
            comboBoxAdapter.Location = new Point(27, 38);
            comboBoxAdapter.Margin = new Padding(2);
            comboBoxAdapter.Name = "comboBoxAdapter";
            comboBoxAdapter.Size = new Size(99, 28);
            comboBoxAdapter.TabIndex = 4;
            // 
            // comboBoxBaud
            // 
            comboBoxBaud.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxBaud.Items.AddRange(new object[] { "250k", "500k" });
            comboBoxBaud.Location = new Point(148, 39);
            comboBoxBaud.Margin = new Padding(2);
            comboBoxBaud.Name = "comboBoxBaud";
            comboBoxBaud.Size = new Size(99, 28);
            comboBoxBaud.TabIndex = 5;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(9F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1003, 516);
            Controls.Add(RxtextBox1);
            Controls.Add(button2);
            Controls.Add(textBox1);
            Controls.Add(comboBoxBaud);
            Controls.Add(comboBoxAdapter);
            Controls.Add(button3);
            Controls.Add(button1);
            Margin = new Padding(2);
            Name = "Form1";
            Text = "Form1";
            ResumeLayout(false);
            PerformLayout();
        }


        #endregion

        private Button button1;
        private TextBox textBox1;
        private Button button2;
        private TextBox RxtextBox1;
        private Button button3;
        private ComboBox comboBoxAdapter;
        private ComboBox comboBoxBaud;
    }
}
