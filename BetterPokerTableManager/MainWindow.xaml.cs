﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BetterPokerTableManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private Config ActiveConfig { get; set; }
        TableManager ActiveTableManager { get; set; }
        private Timer watchOpenTablesTimer = null;

        public bool AskConfirmation(string request, MessageBoxResult defaultResult = MessageBoxResult.No)
        {
            MessageBoxResult dr = MessageBox.Show("Are you sure you want to " + request + "?", "Confirm", 
                MessageBoxButton.YesNo, MessageBoxImage.None, defaultResult);
            return dr == MessageBoxResult.Yes ? true : false;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Set window title to include version
            Title = $"Better Poker Table Manager v{Assembly.GetEntryAssembly().GetName().Version}";

            // Notify application started
            App.Current.Properties["IsRunning"] = true;
            Logger.Log("--- Starting application ---");

            // Load config & install on first run
            ActiveConfig = Config.FromFile();
            DataContext = ActiveConfig;

            // Refresh the profile list & select Active
            RefreshProfileList(selectActive:true);

            // Auto minimize
            if (ActiveConfig.AutoMinimize)
                WindowState = WindowState.Minimized;

            // Start watching open tables
            watchOpenTablesTimer = new Timer(WatchOpenTables, null, 1000, 1000);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Logger.Log("Clean shutdown");
            Thread.Sleep(100);
            App.Current.Properties["IsRunning"] = false;
            Hide();
            Thread.Sleep(2000); // Give threaded loops a few seconds to finish up            
        }


        #region Status
        private void AutoStartCb_Checked(object sender, RoutedEventArgs e)
        {
            // Start table manager if AutoStart is checked after load
            if (IsLoaded && autoStartCb.IsChecked == true && 
                (ActiveTableManager == null || !ActiveTableManager.IsRunning))
                StartStop();
        }

        private void StartStopBtn_Click(object sender, RoutedEventArgs e)
        {
            StartStop();
        }

        private void StartStop()
        {
            // Start table manager
            if (ActiveTableManager == null)
                ActiveTableManager = new TableManager(ActiveConfig);

            if (ActiveTableManager.IsRunning)
            {
                ActiveTableManager.Stop();
                startStopBtn.Content = "Start";
                statusTxt.Text = "Not running";
            }
            else
            {
                ActiveTableManager.Start();
                startStopBtn.Content = "Stop";
                statusTxt.Text = "Running";
            }
        }
        #endregion

        #region Select Profile
        internal void RefreshProfileList(bool selectActive = false, Profile selectSpecific = null)
        {
            List<Profile> newProfileList = Profile.GetAllProfiles();
            if (newProfileList.Count == 0)
                Logger.Log("You have no profiles. This should be impossible. Please restart the application and contact the developer if this occurs again.", Logger.Status.Fatal);

            // Find old value, selectSpecific or find active profile
            Profile requestedSelection = null;
            if (selectActive)
            {
                requestedSelection = newProfileList.FirstOrDefault(p => p.FileName == ActiveConfig.ActiveProfileFileName);
                if (requestedSelection == null) // Can happen when deleting a profile
                    requestedSelection = newProfileList[0];
            }
            else if (selectSpecific != null)
                requestedSelection = newProfileList.FirstOrDefault(p => p.Equals(selectSpecific));
            else if (profileSelectionCb.SelectedValue != null)
                requestedSelection = (Profile)profileSelectionCb.SelectedValue;

            // Bind new list
            profileSelectionCb.ItemsSource = null;
            profileSelectionCb.ItemsSource = newProfileList;


            if (requestedSelection != null && newProfileList.Contains(requestedSelection))
                profileSelectionCb.SelectedIndex = newProfileList.FindIndex(p => p.Equals(requestedSelection));
            else if (newProfileList.Count > 0)
                profileSelectionCb.SelectedIndex = 0;
        }

        private void ProfileSelectionCb_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
                profilePreviewControl.DisplayProfile = (Profile)e.AddedItems[0];
        }

        private void EditProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            Profile selectedProfile = (Profile)profileSelectionCb.SelectedValue;
            if (selectedProfile.FileName == Properties.Settings.Default.DefaultProfileFileName)
            {
                Logger.Log("The default profile cannot be deleted. Try duplicating it instead.", Logger.Status.Info, true);
                return;
            }
            RequestProfileSetup(selectedProfile, SlotConfigHandler.SetupTypes.EditProfile);
        }

        private void NewProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            Profile p = Profile.FromJson(Properties.Resources.profileEmpty);
            p.Name = "New Profile";
            RequestProfileSetup(p, SlotConfigHandler.SetupTypes.NewProfile);
        }

        private void DeleteProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            Profile selectedProfile = (Profile)profileSelectionCb.SelectedValue;
            if (selectedProfile.FileName == Properties.Settings.Default.DefaultProfileFileName)
            {
                Logger.Log("The default profile cannot be deleted.", Logger.Status.Info, true);
                return;
            }
            else if (!AskConfirmation($"DELETE profile '{selectedProfile}'"))
                return;

            string path = System.IO.Path.Combine(Config.DataDir, "Profiles", selectedProfile.FileName);
            try
            {
                System.IO.File.Delete(path);
            }
            catch
            {
                Logger.Log("An error occurred while removing the profile file. "+
                    "Do you have permission? Is the file open elsewhere?", Logger.Status.Error, true);
            }
            RefreshProfileList(selectActive:true);
        }

        private void DuplicateProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            Profile p = (Profile)((Profile)profileSelectionCb.SelectedValue).Clone();
            RequestProfileSetup(p, SlotConfigHandler.SetupTypes.NewProfile);
        }

        private void ImportProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            // Request file from user
            var dlg = new OpenFileDialog();
            dlg.DefaultExt = ".json";
            dlg.Filter = "Profile Files|*.json";
            if (dlg.ShowDialog() == false) // nullable
                return;

            // Generate profile, save it & refresh
            Profile p = Profile.GetProfileFromFile(dlg.FileName);
            p.SaveToFile();
            RefreshProfileList(selectSpecific:p);
        }

        private void ExportProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            Profile p = (Profile)profileSelectionCb.SelectedValue;
            var dlg = new SaveFileDialog();
            dlg.FileName = p.FileName;
            dlg.DefaultExt = ".json";
            dlg.Filter = "Profile Files|*.json";
            if (dlg.ShowDialog() == false) // nullable
                return;
            File.WriteAllText(dlg.FileName, p.GetJson());
        }

        private void RequestProfileSetup(Profile p, SlotConfigHandler.SetupTypes setupType)
        {
            var sch = new SlotConfigHandler(p, setupType);
            sch.ProfileSetupCompleted += Sch_ProfileSetupCompleted;
            sch.StartConfigHandler();
        }

        private void Sch_ProfileSetupCompleted(object sender, EventArgs e)
        {
            var args = (ProfileSetupCompletedEventArgs)e;
            if (!args.IsSaved)
                return;

            // Write to file, refresh and select edited/created profile
            bool overwrite = args.SetupType == SlotConfigHandler.SetupTypes.EditProfile ? true : false;
            args.Profile.SaveToFile(overwrite);
            RefreshProfileList(selectSpecific:args.Profile);
        }

        private void ActivateProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            Profile p = (Profile)profileSelectionCb.SelectedValue;
            ActiveConfig.ActiveProfile = p;
        }
        #endregion

        #region Auto Leave / Table Selection
        private void ApplyAutoLeaveSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            double autoLeaveVpip, autoLeaveHands;
            if (!double.TryParse(autoLeaveVpipTb.Text, out autoLeaveVpip) ||
                !double.TryParse(autoLeaveHandsTb.Text, out autoLeaveHands))
            {
                Logger.Log("Invalid input. Input must be integer (round number).", Logger.Status.Info, showMessageBox: true);
                return;
            }
            else
            {
                ActiveConfig.AutoLeaveVpip = Convert.ToInt32(autoLeaveVpip);
                ActiveConfig.AutoLeaveHands = Convert.ToInt32(autoLeaveHands);
            }
        }
        #endregion

        #region Open Tables
        private void WatchOpenTables(object state)
        {
            try
            {
                Dispatcher.BeginInvoke((Action)delegate () {
                    // Reset list
                    Table selectedTable = (Table)openTablesLv.SelectedValue;
                    openTablesLv.ItemsSource = null;
                    lock (Table.KnownTables)
                    {
                        openTablesLv.ItemsSource = Table.KnownTables.Where(t => !t.IsVirtual);
                        if (selectedTable != null)
                        {
                            int index = openTablesLv.SelectedIndex = Table.KnownTables.FindIndex(t => t == selectedTable);
                            if (index != -1)
                                openTablesLv.SelectedIndex = index;
                        }
                    }
                });
            }
            catch (InvalidOperationException)
            {
                // An ItemsControl is inconsistent with its items source.
                // Just rewrite this whole thing.
                // I think it occurred when opening or closing a bunch of tables at once
                // I doubt the try catch will pick it up regardless..
            }
        }
        #endregion

    }
}
