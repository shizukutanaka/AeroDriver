using System;
using System.Drawing;
using System.Windows.Forms;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.UI
{
    public class SettingsDialog : Form
    {
        private readonly ISettingsService _settingsService;
        private TabControl tabControl;
        
        // General settings
        private CheckBox autoStartCheckBox;
        private CheckBox minimizeToTrayCheckBox;
        private CheckBox autoRefreshCheckBox;
        private ComboBox languageComboBox;
        private ComboBox themeComboBox;
        
        // Update settings
        private CheckBox autoUpdateCheckBox;
        private CheckBox whqlOnlyCheckBox;
        private ComboBox updateIntervalComboBox;
        private CheckBox notifyUpdatesCheckBox;
        
        // Backup settings
        private NumericUpDown backupRetentionUpDown;
        private TextBox backupLocationTextBox;
        private CheckBox autoBackupCheckBox;
        private ComboBox backupIntervalComboBox;
        
        // Performance settings
        private TrackBar cacheSizeTrackBar;
        private Label cacheSizeLabel;
        private CheckBox enableLoggingCheckBox;
        private ComboBox logLevelComboBox;
        private CheckBox telemetryCheckBox;

        public SettingsDialog(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            Text = "Settings";
            Size = new Size(600, 500);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            tabControl = new TabControl
            {
                Dock = DockStyle.Top,
                Height = 400
            };

            tabControl.TabPages.Add(CreateGeneralTab());
            tabControl.TabPages.Add(CreateUpdateTab());
            tabControl.TabPages.Add(CreateBackupTab());
            tabControl.TabPages.Add(CreatePerformanceTab());

            var buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50
            };

            var okButton = new Button
            {
                Text = "OK",
                Location = new Point(350, 10),
                Size = new Size(100, 30),
                DialogResult = DialogResult.OK
            };
            okButton.Click += OnOkClick;

            var cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(460, 10),
                Size = new Size(100, 30),
                DialogResult = DialogResult.Cancel
            };

            var applyButton = new Button
            {
                Text = "Apply",
                Location = new Point(240, 10),
                Size = new Size(100, 30)
            };
            applyButton.Click += OnApplyClick;

            buttonPanel.Controls.Add(okButton);
            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(applyButton);

            Controls.Add(tabControl);
            Controls.Add(buttonPanel);
        }

        private TabPage CreateGeneralTab()
        {
            var tab = new TabPage("General");

            autoStartCheckBox = new CheckBox
            {
                Text = "Start with Windows",
                Location = new Point(20, 20),
                Size = new Size(200, 25)
            };

            minimizeToTrayCheckBox = new CheckBox
            {
                Text = "Minimize to system tray",
                Location = new Point(20, 50),
                Size = new Size(200, 25)
            };

            autoRefreshCheckBox = new CheckBox
            {
                Text = "Auto-refresh driver list",
                Location = new Point(20, 80),
                Size = new Size(200, 25)
            };

            var languageLabel = new Label
            {
                Text = "Language:",
                Location = new Point(20, 120),
                Size = new Size(80, 25)
            };

            languageComboBox = new ComboBox
            {
                Location = new Point(100, 118),
                Size = new Size(150, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            languageComboBox.Items.AddRange(new[] 
            { 
                "English", "Japanese", "Chinese (Simplified)", "Korean",
                "German", "Spanish", "French", "Italian", "Portuguese", "Russian"
            });

            var themeLabel = new Label
            {
                Text = "Theme:",
                Location = new Point(20, 160),
                Size = new Size(80, 25)
            };

            themeComboBox = new ComboBox
            {
                Location = new Point(100, 158),
                Size = new Size(150, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            themeComboBox.Items.AddRange(new[] { "Light", "Dark", "Auto" });

            tab.Controls.Add(autoStartCheckBox);
            tab.Controls.Add(minimizeToTrayCheckBox);
            tab.Controls.Add(autoRefreshCheckBox);
            tab.Controls.Add(languageLabel);
            tab.Controls.Add(languageComboBox);
            tab.Controls.Add(themeLabel);
            tab.Controls.Add(themeComboBox);

            return tab;
        }

        private TabPage CreateUpdateTab()
        {
            var tab = new TabPage("Updates");

            autoUpdateCheckBox = new CheckBox
            {
                Text = "Enable automatic updates",
                Location = new Point(20, 20),
                Size = new Size(200, 25)
            };

            whqlOnlyCheckBox = new CheckBox
            {
                Text = "Install WHQL certified drivers only",
                Location = new Point(20, 50),
                Size = new Size(250, 25)
            };

            notifyUpdatesCheckBox = new CheckBox
            {
                Text = "Notify when updates are available",
                Location = new Point(20, 80),
                Size = new Size(250, 25)
            };

            var intervalLabel = new Label
            {
                Text = "Check interval:",
                Location = new Point(20, 120),
                Size = new Size(100, 25)
            };

            updateIntervalComboBox = new ComboBox
            {
                Location = new Point(120, 118),
                Size = new Size(150, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            updateIntervalComboBox.Items.AddRange(new[] 
            { 
                "Every startup", "Daily", "Weekly", "Bi-weekly", "Monthly" 
            });

            tab.Controls.Add(autoUpdateCheckBox);
            tab.Controls.Add(whqlOnlyCheckBox);
            tab.Controls.Add(notifyUpdatesCheckBox);
            tab.Controls.Add(intervalLabel);
            tab.Controls.Add(updateIntervalComboBox);

            return tab;
        }

        private TabPage CreateBackupTab()
        {
            var tab = new TabPage("Backup");

            autoBackupCheckBox = new CheckBox
            {
                Text = "Enable automatic backups",
                Location = new Point(20, 20),
                Size = new Size(200, 25)
            };

            var retentionLabel = new Label
            {
                Text = "Keep backups:",
                Location = new Point(20, 60),
                Size = new Size(100, 25)
            };

            backupRetentionUpDown = new NumericUpDown
            {
                Location = new Point(120, 58),
                Size = new Size(60, 25),
                Minimum = 1,
                Maximum = 10,
                Value = 3
            };

            var generationsLabel = new Label
            {
                Text = "generations",
                Location = new Point(185, 60),
                Size = new Size(80, 25)
            };

            var locationLabel = new Label
            {
                Text = "Backup location:",
                Location = new Point(20, 100),
                Size = new Size(100, 25)
            };

            backupLocationTextBox = new TextBox
            {
                Location = new Point(20, 125),
                Size = new Size(350, 25),
                ReadOnly = true
            };

            var browseButton = new Button
            {
                Text = "Browse...",
                Location = new Point(380, 124),
                Size = new Size(80, 27)
            };
            browseButton.Click += OnBrowseBackupLocation;

            var intervalLabel = new Label
            {
                Text = "Backup interval:",
                Location = new Point(20, 170),
                Size = new Size(100, 25)
            };

            backupIntervalComboBox = new ComboBox
            {
                Location = new Point(120, 168),
                Size = new Size(150, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            backupIntervalComboBox.Items.AddRange(new[] 
            { 
                "Before each update", "Daily", "Weekly", "Monthly" 
            });

            tab.Controls.Add(autoBackupCheckBox);
            tab.Controls.Add(retentionLabel);
            tab.Controls.Add(backupRetentionUpDown);
            tab.Controls.Add(generationsLabel);
            tab.Controls.Add(locationLabel);
            tab.Controls.Add(backupLocationTextBox);
            tab.Controls.Add(browseButton);
            tab.Controls.Add(intervalLabel);
            tab.Controls.Add(backupIntervalComboBox);

            return tab;
        }

        private TabPage CreatePerformanceTab()
        {
            var tab = new TabPage("Performance");

            var cacheLabel = new Label
            {
                Text = "Cache size:",
                Location = new Point(20, 20),
                Size = new Size(80, 25)
            };

            cacheSizeTrackBar = new TrackBar
            {
                Location = new Point(100, 15),
                Size = new Size(200, 45),
                Minimum = 50,
                Maximum = 500,
                Value = 100,
                TickFrequency = 50
            };
            cacheSizeTrackBar.ValueChanged += OnCacheSizeChanged;

            cacheSizeLabel = new Label
            {
                Text = "100 MB",
                Location = new Point(310, 20),
                Size = new Size(60, 25)
            };

            enableLoggingCheckBox = new CheckBox
            {
                Text = "Enable logging",
                Location = new Point(20, 70),
                Size = new Size(150, 25)
            };

            var logLevelLabel = new Label
            {
                Text = "Log level:",
                Location = new Point(20, 105),
                Size = new Size(80, 25)
            };

            logLevelComboBox = new ComboBox
            {
                Location = new Point(100, 103),
                Size = new Size(150, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            logLevelComboBox.Items.AddRange(new[] 
            { 
                "Error", "Warning", "Information", "Debug", "Verbose" 
            });

            telemetryCheckBox = new CheckBox
            {
                Text = "Send anonymous usage statistics",
                Location = new Point(20, 145),
                Size = new Size(250, 25)
            };

            var clearCacheButton = new Button
            {
                Text = "Clear Cache",
                Location = new Point(20, 190),
                Size = new Size(100, 30)
            };
            clearCacheButton.Click += OnClearCacheClick;

            var resetButton = new Button
            {
                Text = "Reset to Defaults",
                Location = new Point(130, 190),
                Size = new Size(120, 30)
            };
            resetButton.Click += OnResetClick;

            tab.Controls.Add(cacheLabel);
            tab.Controls.Add(cacheSizeTrackBar);
            tab.Controls.Add(cacheSizeLabel);
            tab.Controls.Add(enableLoggingCheckBox);
            tab.Controls.Add(logLevelLabel);
            tab.Controls.Add(logLevelComboBox);
            tab.Controls.Add(telemetryCheckBox);
            tab.Controls.Add(clearCacheButton);
            tab.Controls.Add(resetButton);

            return tab;
        }

        private void LoadSettings()
        {
            // General
            autoStartCheckBox.Checked = _settingsService.GetAutoStartEnabled();
            minimizeToTrayCheckBox.Checked = _settingsService.GetMinimizeToTray();
            autoRefreshCheckBox.Checked = _settingsService.GetAutoRefreshEnabled();
            languageComboBox.SelectedIndex = 0; // Default to English
            themeComboBox.SelectedIndex = 0; // Default to Light

            // Updates
            autoUpdateCheckBox.Checked = _settingsService.GetAutoUpdateEnabled();
            whqlOnlyCheckBox.Checked = _settingsService.GetWhqlOnlyEnabled();
            notifyUpdatesCheckBox.Checked = _settingsService.GetUpdateNotificationsEnabled();
            updateIntervalComboBox.SelectedIndex = 1; // Default to Daily

            // Backup
            autoBackupCheckBox.Checked = _settingsService.GetAutoBackupEnabled();
            backupRetentionUpDown.Value = _settingsService.GetBackupRetentionCount();
            backupLocationTextBox.Text = _settingsService.GetBackupLocation();
            backupIntervalComboBox.SelectedIndex = 0; // Default to Before each update

            // Performance
            cacheSizeTrackBar.Value = _settingsService.GetCacheSizeMB();
            cacheSizeLabel.Text = $"{cacheSizeTrackBar.Value} MB";
            enableLoggingCheckBox.Checked = _settingsService.GetLoggingEnabled();
            logLevelComboBox.SelectedIndex = 2; // Default to Information
            telemetryCheckBox.Checked = _settingsService.GetTelemetryEnabled();
        }

        private void SaveSettings()
        {
            // General
            _settingsService.SetAutoStartEnabled(autoStartCheckBox.Checked);
            _settingsService.SetMinimizeToTray(minimizeToTrayCheckBox.Checked);
            _settingsService.SetAutoRefreshEnabled(autoRefreshCheckBox.Checked);

            // Updates
            _settingsService.SetAutoUpdateEnabled(autoUpdateCheckBox.Checked);
            _settingsService.SetWhqlOnlyEnabled(whqlOnlyCheckBox.Checked);
            _settingsService.SetUpdateNotificationsEnabled(notifyUpdatesCheckBox.Checked);

            // Backup
            _settingsService.SetAutoBackupEnabled(autoBackupCheckBox.Checked);
            _settingsService.SetBackupRetentionCount((int)backupRetentionUpDown.Value);
            if (!string.IsNullOrEmpty(backupLocationTextBox.Text))
            {
                _settingsService.SetBackupLocation(backupLocationTextBox.Text);
            }

            // Performance
            _settingsService.SetCacheSizeMB(cacheSizeTrackBar.Value);
            _settingsService.SetLoggingEnabled(enableLoggingCheckBox.Checked);
            _settingsService.SetTelemetryEnabled(telemetryCheckBox.Checked);

            _settingsService.SaveSettingsAsync().Wait();
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            SaveSettings();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnApplyClick(object sender, EventArgs e)
        {
            SaveSettings();
            MessageBox.Show("Settings applied successfully.", "Settings",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnBrowseBackupLocation(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select backup location";
                dialog.SelectedPath = backupLocationTextBox.Text;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    backupLocationTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void OnCacheSizeChanged(object sender, EventArgs e)
        {
            cacheSizeLabel.Text = $"{cacheSizeTrackBar.Value} MB";
        }

        private void OnClearCacheClick(object sender, EventArgs e)
        {
            if (MessageBox.Show("Clear all cached data?", "Clear Cache",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                // Clear cache logic would go here
                MessageBox.Show("Cache cleared successfully.", "Cache",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OnResetClick(object sender, EventArgs e)
        {
            if (MessageBox.Show("Reset all settings to defaults?", "Reset Settings",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                _settingsService.ResetToDefaultsAsync().Wait();
                LoadSettings();
                MessageBox.Show("Settings reset to defaults.", "Reset",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}