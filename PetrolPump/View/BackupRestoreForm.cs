using ZaibPetroleumService.ProjectConnection;
using ZaibPetroleumService.Services;
using System;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.IO;
using System.Windows.Forms;

namespace ZaibPetroleumService.View
{
    public partial class BackupRestoreForm : Sample
    {
        public BackupRestoreForm()
        {
            InitializeComponent();
        }

        public static string ConnectionString => conReturn();

        // Method to return the connection string for SQLite
        public static string conReturn()
        {
            string dbFolderPathAppData = LocalAppStorage.GetLocalDataDirectory();
            string dbFilePath = LocalAppStorage.GetDatabaseFilePath();

            // Ensure directory exists and the database file exists
            if (!File.Exists(dbFilePath))
            {
                if (!Directory.Exists(dbFolderPathAppData))
                    Directory.CreateDirectory(dbFolderPathAppData);

                using (FileStream fs = File.Create(dbFilePath))
                {
                    fs.Close();
                }
            }

            return $@"Data Source={dbFilePath};Version=3;";
        }

        // Backup Button: Backs up the database
        private void btnBackup_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtBackupPath.Text))
            {
                MessageBox.Show("Please select a valid backup path.");
                return;
            }

            string backupFilePath = Path.Combine(txtBackupPath.Text, LocalAppStorage.DatabaseFileName.Replace(".db", "_backup.db"));

            try
            {
                BackupDatabase(backupFilePath);
                MessageBox.Show("Backup completed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during backup: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Restore Button: Restores the database from a backup file
        private void btnRestore_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtRestorePath.Text))
            {
                MessageBox.Show("Please select a valid restore file.");
                return;
            }

            try
            {
                RestoreDatabase(txtRestorePath.Text);
                MessageBox.Show("Restore completed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during restore: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Method to backup the SQLite database file to a new location using SQLite Backup API
        public static void BackupDatabase(string backupFilePath)
        {
            string databaseFilePath = LocalAppStorage.GetDatabaseFilePath();

            try
            {
                using (SQLiteConnection sourceConnection = new SQLiteConnection($@"Data Source={databaseFilePath};Version=3;"))
                using (SQLiteConnection backupConnection = new SQLiteConnection($@"Data Source={backupFilePath};Version=3;"))
                {
                    sourceConnection.Open();
                    backupConnection.Open();

                    // Start the backup process
                    sourceConnection.BackupDatabase(backupConnection, "main", "main", -1, null, 0);

                    MessageBox.Show("Backup completed successfully.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during backup: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Method to restore the SQLite database file from a backup
        public static void RestoreDatabase(string restoreFilePath)
        {
            string databaseFilePath = LocalAppStorage.GetDatabaseFilePath();

            try
            {
                using (SQLiteConnection con = new SQLiteConnection($@"Data Source={databaseFilePath};Version=3;"))
                {
                    con.Open();

                    // Perform restore operation by overwriting the database with the backup
                    File.Copy(restoreFilePath, databaseFilePath, true);
                    projectconnection.EnsureSchemaUpdates(databaseFilePath);

                    MessageBox.Show("Restore completed successfully.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during restore: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Close the form (optional)
        private void guna2Button1_Click(object sender, EventArgs e)
        {
            this.Hide();
        }



        private void btnBrowseBackup_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderBrowser = new FolderBrowserDialog())
            {
                if (folderBrowser.ShowDialog() == DialogResult.OK)
                {
                    txtBackupPath.Text = folderBrowser.SelectedPath;
                }
            }
        }

        // Browse Button for restore file
        private void btnBrowseRestore_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "SQLite Database Files (*.db)|*.db";
                openFileDialog.Title = "Select the SQLite Database File";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    txtRestorePath.Text = openFileDialog.FileName;
                }
            }
        }
    }
}
