using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Framework.Hashing;
using OfficeOpenXml;

namespace SignatureVerificator
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                label2.Text = openFileDialog1.FileName;
                label2.Visible = true;
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (openFileDialog2.ShowDialog() == DialogResult.OK)
            {
                using (var stream = new FileStream(openFileDialog2.FileName, FileMode.Open))
                {
                    using (var package = new ExcelPackage(stream))
                    {
                        var sheet = package.Workbook.Worksheets.First();
                        var table = new DataTable();
                        for (int i = 1; i <= sheet.Dimension.End.Column; i++)
                        {
                            var val = sheet.Cells[1, i].Value;
                            if (val != null)
                            {
                                table.Columns.Add(val.ToString());
                            }
                        }
                        for (int row = 2; row <= sheet.Dimension.End.Row; row++)
                        {
                            if (sheet.Cells[row, 1].Value == null)
                            {
                                continue;
                            }
                            var tablerow = table.NewRow();
                            for (int i = 0; i < table.Columns.Count; i++)
                            {
                                if (sheet.Cells[row, i + 1].Value.ToString() != "NULL" && sheet.Cells[row, i + 1].Value != null &&
                                    !string.IsNullOrEmpty(sheet.Cells[row, i + 1].Value.ToString()))
                                {
                                    tablerow[i] = sheet.Cells[row, i + 1].Value.ToString();
                                }
                                else
                                {
                                    tablerow[i] = string.Empty;
                                }
                            }

                            table.Rows.Add(tablerow);
                        }

                        dataGridView1.DataSource = table;
                    }
                }
            }
        }

        private async void button3_Click(object sender, EventArgs e)
        {
            button3.Enabled = false;
            var table = (DataTable)dataGridView1.DataSource;
            int validCount = 0;
            int invalidCount = 0;
            foreach (DataRow row in ((DataTable)dataGridView1.DataSource).Rows)
            {
                var vals = new string[table.Columns.Count];
                vals[0] = textBox1.Text;
                for (int i = 1; i < table.Columns.Count; i++)
                {
                    vals[i] = row[i].ToString();
                }

                bool isValid = false;

                await Task.Run(() =>
                    {
                        isValid = DigitalSignature.VerifySignature(openFileDialog1.FileName, Convert.FromBase64String(row[0].ToString()), vals);
                    });

                dataGridView1.Rows[table.Rows.IndexOf(row)].DefaultCellStyle.BackColor = isValid ? Color.Green : Color.Red;
                if (isValid)
                {
                    validCount++;
                }
                else
                {
                    invalidCount++;
                }
            }

            button3.Enabled = true;
            lblValidCount.Text = "Teisingi: " + validCount;
            lblValidCount.Visible = true;
            lblInvalidCount.Text = "Neteisingi: " + invalidCount;
            lblInvalidCount.Visible = true;
        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                textBox1.SelectAll();
            }
        }
    }
}
