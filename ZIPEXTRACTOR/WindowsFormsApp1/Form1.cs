using System;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Configuration;  // Needed for ConfigurationManager
using MySql.Data.MySqlClient;

namespace WindowsFormsApp1
{
    public partial class Form1 : Form
    {
        private string currentFolder = string.Empty;
        private string outFolder = string.Empty;
        private string selectedZip = string.Empty;
        private string destPath = "D:\\data\\download";
        private readonly string connectionString = ConfigurationManager.ConnectionStrings["ZipDb"].ConnectionString;

        public Form1()
        {
            InitializeComponent();
            OutputLabel.Text = destPath;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            progressBar1.Visible = false;
        }

        private void btnSelectFolder_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    currentFolder = dialog.SelectedPath;
                    lblFolder.Text = "Folder: " + currentFolder;
                    
                    
                    RefreshZipList();
                }
            }
        }

        private void RefreshZipList()
        {
            lstZips.Items.Clear();

            if (Directory.Exists(currentFolder))
            {
                var zips = Directory.GetFiles(currentFolder, "*.zip");
                foreach (var z in zips)
                    lstZips.Items.Add(Path.GetFileName(z));

                if (zips.Length == 0)
                    MessageBox.Show("No ZIP files found in this folder.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void lstZips_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstZips.SelectedItem != null)
            {
                selectedZip = lstZips.SelectedItem.ToString();
                lstFiles.Items.Clear();
            }
        }

        private async void btnExtract_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(selectedZip))
            {
                MessageBox.Show("Please select a ZIP file first.");
                return;
            }

            string sourceZip = Path.Combine(currentFolder, selectedZip);
            //  string downloadsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");
            if (!Directory.Exists(destPath)) {
                Directory.CreateDirectory(destPath);
            }
           

            string copiedZip = Path.Combine(destPath, selectedZip);
            File.Copy(sourceZip, copiedZip, true);

           // string extractFolder = Path.Combine(downloadsFolder, Path.GetFileNameWithoutExtension(selectedZip));
            string extractFolder = Path.Combine(destPath);
            Directory.CreateDirectory(extractFolder);

            progressBar1.Visible = true;
            progressBar1.Value = 0;

            await Task.Run(() =>
            {
                using (ZipArchive archive = ZipFile.OpenRead(copiedZip))
                {
                    int total = archive.Entries.Count;
                    int current = 0;

                    foreach (var entry in archive.Entries)
                    {
                        string destination = Path.Combine(extractFolder, entry.FullName);
                        string dir = Path.GetDirectoryName(destination);

                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);

                        if (!string.IsNullOrEmpty(entry.Name))
                            entry.ExtractToFile(destination, true);

                        current++;
                        int progress = (int)((current / (double)total) * 100);

                        // Marshal UI update
                        this.Invoke(new Action(() =>
                        {
                            progressBar1.Value = progress;
                        }));
                    }
                }
            });
            File.Delete(copiedZip);
            progressBar1.Visible = false;
            MessageBox.Show("Extraction complete!\n\nSaved to: " + extractFolder, "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ListExtractedFiles(extractFolder);
            long zipId = GetNextZipId();
            InsertFileRecords(zipId, extractFolder);
            MessageBox.Show(
                    $"File extraction completed. please close the application !!!!",
                    "Task Completed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
        }
        private void InsertFileRecords(long zipId, string extractFolder)
        {
            try
            {
                
                string status = "Pending";
                DateTime insertAt = DateTime.Now;

                string[] pdfFiles = Directory.GetFiles(extractFolder, "*.pdf", SearchOption.AllDirectories);

                if (pdfFiles.Length == 0)
                {
                    MessageBox.Show("No PDF files found in extracted folder.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (var conn = new MySqlConnection(connectionString))
                {
                    conn.Open();

                    // Declare transaction outside 'using' so we can commit/recreate manually
                    MySqlTransaction transaction = conn.BeginTransaction();

                    string query = @"INSERT INTO request_jobs 
                             (zip_id, file_id, request_id, file_path, status, insert_at)
                             VALUES (@zip_id, @file_id, @request_id, @file_path, @status, @insert_at)";

                    using (var cmd = new MySqlCommand(query, conn, transaction))
                    {
                        cmd.Parameters.Add("@zip_id", MySqlDbType.Int64);
                        cmd.Parameters.Add("@file_id", MySqlDbType.Int64);
                        cmd.Parameters.Add("@request_id", MySqlDbType.VarChar);
                        cmd.Parameters.Add("@file_path", MySqlDbType.VarChar);
                        cmd.Parameters.Add("@status", MySqlDbType.VarChar);
                        cmd.Parameters.Add("@insert_at", MySqlDbType.Timestamp);

                        long fileCounter = 1;
                        int batchSize = 1000;

                        foreach (string file in pdfFiles)
                        {
                            string requestId = Guid.NewGuid().ToString("N");
                            cmd.Parameters["@zip_id"].Value = zipId;
                            cmd.Parameters["@file_id"].Value = fileCounter++;
                            cmd.Parameters["@request_id"].Value = requestId;
                            cmd.Parameters["@file_path"].Value = file;
                            cmd.Parameters["@status"].Value = status;
                            cmd.Parameters["@insert_at"].Value = insertAt;

                            cmd.ExecuteNonQuery();

                            //  Commit and start new transaction every batch
                            if (fileCounter % batchSize == 0)
                            {
                                transaction.Commit();
                                transaction.Dispose();
                                transaction = conn.BeginTransaction();
                                cmd.Transaction = transaction;
                            }
                        }

                        // Commit remaining records
                        transaction.Commit();
                    }
                }

                MessageBox.Show(
                    $"ZIP ID: {zipId}\nInserted {pdfFiles.Length:N0} PDF file records successfully.",
                    "Database Insert",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                using (var conn = new MySqlConnection(connectionString))
                {
                    conn.Open();

                    // Declare transaction outside 'using' so we can commit/recreate manually
                    MySqlTransaction transaction = conn.BeginTransaction();

                    string query = @"INSERT INTO control_summary 
                             (zip_id, file_count)
                             VALUES (@zip_id, @file_count)";

                    using (var cmd = new MySqlCommand(query, conn, transaction))
                    {
                        cmd.Parameters.Add("@zip_id", MySqlDbType.Int64);
                        cmd.Parameters.Add("@file_count", MySqlDbType.Int64);
                        

                            cmd.Parameters["@zip_id"].Value = zipId;
                        cmd.Parameters["@file_count"].Value = pdfFiles.Length;


                            cmd.ExecuteNonQuery();

                        
                            
                                transaction.Commit();
                                transaction.Dispose();
                                transaction = conn.BeginTransaction();
                                cmd.Transaction = transaction;
                           
                        
                        transaction.Commit();
                    }
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show("Database insert failed:\n" + ex.Message,
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }








        private long GetNextZipId()
        {
            long newZipId = 1;

            using (var conn = new MySqlConnection(connectionString))
            {
                conn.Open();

                // 1️ Read current value
                string selectQuery = "SELECT control_value FROM control_table WHERE control_id = 1";
                using (var selectCmd = new MySqlCommand(selectQuery, conn))
                {
                    object result = selectCmd.ExecuteScalar();
                    long currentValue = 0;
                    if (result != DBNull.Value && result != null)
                        currentValue = Convert.ToInt64(result);

                    newZipId = currentValue + 1;

                    // 2️ Update new value back
                    string updateQuery = "UPDATE control_table SET control_value = @val WHERE control_id = 1";
                    using (var updateCmd = new MySqlCommand(updateQuery, conn))
                    {
                        updateCmd.Parameters.AddWithValue("@val", newZipId);
                        updateCmd.ExecuteNonQuery();
                    }
                }
            }

            return newZipId;
        }

        private void ListExtractedFiles(string folder)
        {
            lstFiles.Items.Clear();
            var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                                 .Select(f => GetRelativePath(folder, f));

            foreach (var f in files)
                lstFiles.Items.Add(f);
        }



        private static string GetRelativePath(string basePath, string fullPath)
        {
            Uri baseUri = new Uri(AppendDirectorySeparatorChar(basePath));
            Uri fullUri = new Uri(fullPath);

            if (baseUri.Scheme != fullUri.Scheme)
                return fullPath; // path can't be made relative

            Uri relativeUri = baseUri.MakeRelativeUri(fullUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            if (string.Equals(fullUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            return relativePath;
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            // Append a slash only if it’s a directory and doesn’t already end with one
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
                return path + Path.DirectorySeparatorChar;
            return path;
        }

        private void OutputFolder_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    outFolder = dialog.SelectedPath;
                    OutputLabel.Text = "Folder: " + outFolder;
                    destPath = outFolder;

                    RefreshZipList();
                }
            }
        }
    }
}
