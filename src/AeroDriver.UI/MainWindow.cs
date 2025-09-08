using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AeroDriver.UI
{
    public partial class MainWindow : Form
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IDriverService _driverService;
        private readonly IBackupService _backupService;
        private readonly ISystemHealthService _systemHealthService;
        private readonly ICleanupService _cleanupService;
        private readonly IAutoUpdateService _autoUpdateService;
        private readonly ISettingsService _settingsService;
        
        private TabControl tabControl;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;
        private ToolStripProgressBar progressBar;
        private System.Windows.Forms.Timer refreshTimer;

        public MainWindow(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _driverService = serviceProvider.GetRequiredService<IDriverService>();
            _backupService = serviceProvider.GetRequiredService<IBackupService>();
            _systemHealthService = serviceProvider.GetRequiredService<ISystemHealthService>();
            _cleanupService = serviceProvider.GetRequiredService<ICleanupService>();
            _autoUpdateService = serviceProvider.GetRequiredService<IAutoUpdateService>();
            _settingsService = serviceProvider.GetRequiredService<ISettingsService>();
            
            InitializeComponent();
            InitializeTimer();
        }

        private void InitializeComponent()
        {
            Text = "AeroDriver - Windows Driver Management";
            Size = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Application;

            // Create main menu
            var menuStrip = new MenuStrip();
            var fileMenu = new ToolStripMenuItem("File");
            fileMenu.DropDownItems.Add("Settings", null, OnSettingsClick);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("Exit", null, (s, e) => Application.Exit());
            
            var toolsMenu = new ToolStripMenuItem("Tools");
            toolsMenu.DropDownItems.Add("System Health Check", null, OnHealthCheckClick);
            toolsMenu.DropDownItems.Add("Cleanup", null, OnCleanupClick);
            toolsMenu.DropDownItems.Add("Backup Manager", null, OnBackupManagerClick);
            
            var helpMenu = new ToolStripMenuItem("Help");
            helpMenu.DropDownItems.Add("Documentation", null, OnDocumentationClick);
            helpMenu.DropDownItems.Add("About", null, OnAboutClick);
            
            menuStrip.Items.Add(fileMenu);
            menuStrip.Items.Add(toolsMenu);
            menuStrip.Items.Add(helpMenu);
            
            // Create tab control
            tabControl = new TabControl
            {
                Dock = DockStyle.Fill
            };
            
            // Add tabs
            tabControl.TabPages.Add(CreateDriversTab());
            tabControl.TabPages.Add(CreateUpdatesTab());
            tabControl.TabPages.Add(CreateBackupTab());
            tabControl.TabPages.Add(CreateHealthTab());
            
            // Create status strip
            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("Ready")
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            progressBar = new ToolStripProgressBar
            {
                Visible = false,
                Style = ProgressBarStyle.Marquee
            };
            statusStrip.Items.Add(statusLabel);
            statusStrip.Items.Add(progressBar);
            
            // Add controls
            Controls.Add(tabControl);
            Controls.Add(statusStrip);
            Controls.Add(menuStrip);
            MainMenuStrip = menuStrip;
        }

        private TabPage CreateDriversTab()
        {
            var tab = new TabPage("Drivers");
            
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50
            };
            
            var scanButton = new Button
            {
                Text = "Scan for Updates",
                Location = new Point(10, 10),
                Size = new Size(120, 30)
            };
            scanButton.Click += OnScanClick;
            
            var updateAllButton = new Button
            {
                Text = "Update All",
                Location = new Point(140, 10),
                Size = new Size(120, 30),
                Enabled = false
            };
            updateAllButton.Click += OnUpdateAllClick;
            
            panel.Controls.Add(scanButton);
            panel.Controls.Add(updateAllButton);
            
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            
            listView.Columns.Add("Device", 250);
            listView.Columns.Add("Current Version", 120);
            listView.Columns.Add("Available Version", 120);
            listView.Columns.Add("Status", 100);
            listView.Columns.Add("WHQL Certified", 100);
            
            tab.Controls.Add(listView);
            tab.Controls.Add(panel);
            tab.Tag = new { ListView = listView, UpdateButton = updateAllButton };
            
            return tab;
        }

        private TabPage CreateUpdatesTab()
        {
            var tab = new TabPage("Auto Update");
            
            var groupBox = new GroupBox
            {
                Text = "Automatic Update Settings",
                Location = new Point(10, 10),
                Size = new Size(400, 200)
            };
            
            var enableCheckBox = new CheckBox
            {
                Text = "Enable automatic updates",
                Location = new Point(20, 30),
                Size = new Size(200, 25),
                Checked = _settingsService.GetAutoUpdateEnabled()
            };
            enableCheckBox.CheckedChanged += OnAutoUpdateToggle;
            
            var intervalLabel = new Label
            {
                Text = "Check interval:",
                Location = new Point(20, 70),
                Size = new Size(100, 25)
            };
            
            var intervalCombo = new ComboBox
            {
                Location = new Point(120, 68),
                Size = new Size(150, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            intervalCombo.Items.AddRange(new[] { "Daily", "Weekly", "Monthly" });
            intervalCombo.SelectedIndex = 0;
            
            var whqlOnlyCheckBox = new CheckBox
            {
                Text = "Install WHQL certified drivers only",
                Location = new Point(20, 110),
                Size = new Size(250, 25),
                Checked = true
            };
            
            var checkNowButton = new Button
            {
                Text = "Check Now",
                Location = new Point(20, 150),
                Size = new Size(100, 30)
            };
            checkNowButton.Click += OnCheckNowClick;
            
            groupBox.Controls.Add(enableCheckBox);
            groupBox.Controls.Add(intervalLabel);
            groupBox.Controls.Add(intervalCombo);
            groupBox.Controls.Add(whqlOnlyCheckBox);
            groupBox.Controls.Add(checkNowButton);
            
            var historyListBox = new ListBox
            {
                Location = new Point(10, 220),
                Size = new Size(400, 150)
            };
            
            tab.Controls.Add(groupBox);
            tab.Controls.Add(historyListBox);
            
            return tab;
        }

        private TabPage CreateBackupTab()
        {
            var tab = new TabPage("Backup & Restore");
            
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50
            };
            
            var createBackupButton = new Button
            {
                Text = "Create Backup",
                Location = new Point(10, 10),
                Size = new Size(120, 30)
            };
            createBackupButton.Click += OnCreateBackupClick;
            
            var restoreButton = new Button
            {
                Text = "Restore",
                Location = new Point(140, 10),
                Size = new Size(120, 30)
            };
            restoreButton.Click += OnRestoreClick;
            
            var deleteOldButton = new Button
            {
                Text = "Clean Old Backups",
                Location = new Point(270, 10),
                Size = new Size(120, 30)
            };
            deleteOldButton.Click += OnDeleteOldBackupsClick;
            
            panel.Controls.Add(createBackupButton);
            panel.Controls.Add(restoreButton);
            panel.Controls.Add(deleteOldButton);
            
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            
            listView.Columns.Add("Backup Name", 200);
            listView.Columns.Add("Date Created", 150);
            listView.Columns.Add("Size", 100);
            listView.Columns.Add("Drivers", 80);
            listView.Columns.Add("Description", 200);
            
            tab.Controls.Add(listView);
            tab.Controls.Add(panel);
            tab.Tag = listView;
            
            return tab;
        }

        private TabPage CreateHealthTab()
        {
            var tab = new TabPage("System Health");
            
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50
            };
            
            var checkHealthButton = new Button
            {
                Text = "Run Health Check",
                Location = new Point(10, 10),
                Size = new Size(120, 30)
            };
            checkHealthButton.Click += OnHealthCheckClick;
            
            var exportButton = new Button
            {
                Text = "Export Report",
                Location = new Point(140, 10),
                Size = new Size(120, 30)
            };
            exportButton.Click += OnExportHealthReportClick;
            
            panel.Controls.Add(checkHealthButton);
            panel.Controls.Add(exportButton);
            
            var textBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 9)
            };
            
            tab.Controls.Add(textBox);
            tab.Controls.Add(panel);
            tab.Tag = textBox;
            
            return tab;
        }

        private void InitializeTimer()
        {
            refreshTimer = new System.Windows.Forms.Timer
            {
                Interval = 30000 // 30 seconds
            };
            refreshTimer.Tick += OnRefreshTimer;
            refreshTimer.Start();
        }

        private async void OnScanClick(object sender, EventArgs e)
        {
            try
            {
                SetStatus("Scanning for driver updates...", true);
                var driversTab = tabControl.TabPages[0];
                var controls = (dynamic)driversTab.Tag;
                ListView listView = controls.ListView;
                Button updateButton = controls.UpdateButton;
                
                listView.Items.Clear();
                
                var drivers = await _driverService.GetAllDriversAsync();
                
                foreach (var driver in drivers)
                {
                    var item = new ListViewItem(new[]
                    {
                        driver.DeviceName,
                        driver.CurrentVersion,
                        driver.AvailableVersion ?? "N/A",
                        driver.Status.ToString(),
                        driver.IsWhqlCertified ? "Yes" : "No"
                    });
                    
                    if (driver.HasUpdate)
                    {
                        item.BackColor = Color.LightYellow;
                    }
                    
                    listView.Items.Add(item);
                }
                
                updateButton.Enabled = drivers.Any(d => d.HasUpdate);
                SetStatus($"Found {drivers.Count(d => d.HasUpdate)} driver updates available");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning for drivers: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Scan failed");
            }
        }

        private async void OnUpdateAllClick(object sender, EventArgs e)
        {
            if (MessageBox.Show("Update all drivers with available updates?", "Confirm Update",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try
                {
                    SetStatus("Updating drivers...", true);
                    await _driverService.UpdateAllDriversAsync();
                    SetStatus("All drivers updated successfully");
                    OnScanClick(sender, e); // Refresh the list
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error updating drivers: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetStatus("Update failed");
                }
            }
        }

        private async void OnHealthCheckClick(object sender, EventArgs e)
        {
            try
            {
                SetStatus("Running system health check...", true);
                var healthTab = tabControl.TabPages[3];
                var textBox = (RichTextBox)healthTab.Tag;
                
                var report = await _systemHealthService.GetHealthReportAsync();
                
                textBox.Clear();
                textBox.AppendText($"System Health Report\n");
                textBox.AppendText($"Generated: {DateTime.Now}\n\n");
                
                textBox.AppendText($"Overall Status: {report.OverallStatus}\n");
                textBox.AppendText($"Score: {report.HealthScore}/100\n\n");
                
                textBox.AppendText("Issues Found:\n");
                foreach (var issue in report.Issues)
                {
                    textBox.AppendText($"  - {issue.Description} (Severity: {issue.Severity})\n");
                }
                
                textBox.AppendText("\nRecommendations:\n");
                foreach (var rec in report.Recommendations)
                {
                    textBox.AppendText($"  - {rec}\n");
                }
                
                SetStatus("Health check completed");
                tabControl.SelectedIndex = 3; // Switch to health tab
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error running health check: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Health check failed");
            }
        }

        private async void OnCleanupClick(object sender, EventArgs e)
        {
            if (MessageBox.Show("Run system cleanup?", "Confirm Cleanup",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try
                {
                    SetStatus("Running cleanup...", true);
                    var result = await _cleanupService.RunFullCleanupAsync();
                    
                    MessageBox.Show(
                        $"Cleanup completed!\n\n" +
                        $"Files removed: {result.FilesRemoved}\n" +
                        $"Space freed: {result.SpaceFreed / 1024 / 1024} MB",
                        "Cleanup Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    
                    SetStatus("Cleanup completed");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error during cleanup: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetStatus("Cleanup failed");
                }
            }
        }

        private async void OnCreateBackupClick(object sender, EventArgs e)
        {
            try
            {
                SetStatus("Creating backup...", true);
                var backupPath = await _backupService.CreateBackupAsync("Manual backup");
                
                MessageBox.Show($"Backup created successfully!\n\nLocation: {backupPath}",
                    "Backup Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                RefreshBackupList();
                SetStatus("Backup created");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating backup: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Backup failed");
            }
        }

        private async void OnRestoreClick(object sender, EventArgs e)
        {
            var backupTab = tabControl.TabPages[2];
            var listView = (ListView)backupTab.Tag;
            
            if (listView.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select a backup to restore.", "No Selection",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            
            if (MessageBox.Show("Restore selected backup?", "Confirm Restore",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                try
                {
                    SetStatus("Restoring backup...", true);
                    var backupName = listView.SelectedItems[0].Text;
                    await _backupService.RestoreBackupAsync(backupName);
                    
                    MessageBox.Show("Backup restored successfully!", "Restore Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    
                    SetStatus("Backup restored");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error restoring backup: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetStatus("Restore failed");
                }
            }
        }

        private async void OnDeleteOldBackupsClick(object sender, EventArgs e)
        {
            try
            {
                SetStatus("Cleaning old backups...", true);
                await _backupService.CleanupOldBackupsAsync();
                
                MessageBox.Show("Old backups cleaned successfully!", "Cleanup Complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                RefreshBackupList();
                SetStatus("Backup cleanup completed");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cleaning backups: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Cleanup failed");
            }
        }

        private async void OnCheckNowClick(object sender, EventArgs e)
        {
            try
            {
                SetStatus("Checking for updates...", true);
                var result = await _autoUpdateService.CheckForUpdatesAsync();
                
                if (result.UpdatesAvailable > 0)
                {
                    MessageBox.Show($"Found {result.UpdatesAvailable} updates available!",
                        "Updates Available", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("No updates available.", "Up to Date",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                
                SetStatus("Update check completed");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error checking for updates: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Update check failed");
            }
        }

        private void OnAutoUpdateToggle(object sender, EventArgs e)
        {
            var checkBox = (CheckBox)sender;
            _settingsService.SetAutoUpdateEnabled(checkBox.Checked);
        }

        private void OnSettingsClick(object sender, EventArgs e)
        {
            using (var dialog = new SettingsDialog(_settingsService))
            {
                dialog.ShowDialog(this);
            }
        }

        private void OnBackupManagerClick(object sender, EventArgs e)
        {
            tabControl.SelectedIndex = 2; // Switch to backup tab
        }

        private void OnDocumentationClick(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/yourusername/aerodriver/wiki");
        }

        private void OnAboutClick(object sender, EventArgs e)
        {
            MessageBox.Show(
                "AeroDriver v1.0.0\n\n" +
                "Windows Driver Management Tool\n" +
                "© 2025 AeroDriver Team\n\n" +
                "Licensed under MIT License",
                "About AeroDriver",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OnExportHealthReportClick(object sender, EventArgs e)
        {
            using (var saveDialog = new SaveFileDialog())
            {
                saveDialog.Filter = "HTML files (*.html)|*.html|Text files (*.txt)|*.txt";
                saveDialog.DefaultExt = "html";
                
                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var healthTab = tabControl.TabPages[3];
                        var textBox = (RichTextBox)healthTab.Tag;
                        System.IO.File.WriteAllText(saveDialog.FileName, textBox.Text);
                        
                        MessageBox.Show("Report exported successfully!", "Export Complete",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error exporting report: {ex.Message}", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private async void RefreshBackupList()
        {
            try
            {
                var backupTab = tabControl.TabPages[2];
                var listView = (ListView)backupTab.Tag;
                listView.Items.Clear();
                
                var backups = await _backupService.GetAllBackupsAsync();
                
                foreach (var backup in backups)
                {
                    var item = new ListViewItem(new[]
                    {
                        backup.Name,
                        backup.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        $"{backup.SizeInBytes / 1024 / 1024} MB",
                        backup.DriverCount.ToString(),
                        backup.Description
                    });
                    listView.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error loading backups: {ex.Message}");
            }
        }

        private void OnRefreshTimer(object sender, EventArgs e)
        {
            // Periodic refresh of data
            if (_settingsService.GetAutoRefreshEnabled())
            {
                RefreshBackupList();
            }
        }

        private void SetStatus(string message, bool showProgress = false)
        {
            statusLabel.Text = message;
            progressBar.Visible = showProgress;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RefreshBackupList();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            refreshTimer?.Stop();
            refreshTimer?.Dispose();
            base.OnFormClosing(e);
        }
    }
}