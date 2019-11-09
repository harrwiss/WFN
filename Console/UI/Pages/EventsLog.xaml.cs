﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Wokhan.WindowsFirewallNotifier.Common.Helpers;
using Wokhan.WindowsFirewallNotifier.Console.Helpers.ViewModels;
using System.ComponentModel;
using System.Data;
using System.Windows.Data;
using System.Windows.Media;
using Harrwiss.Common.Network.Helper;
using System.Net;
using System.Text.RegularExpressions;
using Wokhan.WindowsFirewallNotifier.Common;

namespace Wokhan.WindowsFirewallNotifier.Console.UI.Pages
{
    /// <summary>
    /// Interaction logic for EventLog.xaml
    /// </summary>
    public partial class EventsLog : Page
    {
        private const int MaxEventsToLoad = 1500;

        private Dictionary<int, ProcessHelper.ServiceInfoResult> services = ProcessHelper.GetAllServicesByPidWMI();

        public bool IsTrackingEnabled
        {
            get { return timer.IsEnabled; }
            set { timer.IsEnabled = value; }
        }

        public bool IsTCPOnlyEnabled { 
            get {
                return Settings.Default.FilterTcpOnlyEvents;
            }
            set {
                Settings.Default.FilterTcpOnlyEvents = value;
                Settings.Default.Save();
            } 
        }

        private DispatcherTimer timer = new DispatcherTimer() { IsEnabled = true };

        public List<int> Intervals { get { return new List<int> { 1, 5, 10 }; } }

        private int _interval = 1;
        public int Interval
        {
            get { return _interval; }
            set { _interval = value; timer.Interval = TimeSpan.FromSeconds(value); }
        }

        private bool RefreshFilterData = true;

        public EventsLog()
        {
            InitializeComponent();

            if (!Settings.Default.EnableDnsResolver)
            {
                RemoteHostCol.Visibility = Visibility.Hidden;
            }

            if (((App)Application.Current).IsElevated)
            {
                timer.Interval = TimeSpan.FromSeconds(Interval);
                timer.Tick += timer_Tick;

                this.Loaded += EventsLog_Loaded;
                this.Unloaded += EventsLog_Unloaded;
            }
        }

        private void EventsLog_Unloaded(object sender, RoutedEventArgs e)
        {
            timer.Stop();
        }

        void EventsLog_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.InvokeAsync(() => timer_Tick(null, null));
        }

        void timer_Tick(object sender, EventArgs e)
        {
            initEventLog();
        }


        
        private ObservableCollection<LogEntryViewModel> _logEntries = new ObservableCollection<LogEntryViewModel>();
        public ObservableCollection<LogEntryViewModel> LogEntries { get {
                return _logEntries;
            }
        }  

        private DateTime lastDate = DateTime.MinValue;
        private void initEventLog()
        {
            // small re-write because it got stuck going through all event logs when it found no matching event e.g. 5157
            try
            {
                using (EventLog securityLog = new EventLog("security"))
                {
                    // TODO: utilize EventLog#EnableRaisingEvents after initialization instead of timer
                    //securityLog.EnableRaisingEvents = true;
                    //securityLog.EntryWritten += (sender, args) => _logEntries.Add(createEventLogEntry(args.Entry));

                    int slCount = securityLog.Entries.Count - 1;
                    int eventsStored = 0;
                    bool isAppending = _logEntries.Any();
                    DateTime lastDateNew = lastDate;
                    List<String> tcpIPList = new List<string>();

                    for (int i = slCount; i > 0 && eventsStored < MaxEventsToLoad; i--)
                    {
                        EventLogEntry entry = securityLog.Entries[i];

                        if (lastDate != DateTime.MinValue && entry.TimeWritten <= lastDate)
                        {
                            break;
                        }

                        // Note: instanceId == eventID
                        if (FirewallHelper.isEventInstanceIdAccepted(entry.InstanceId))
                        {
                            LogEntryViewModel lastEntry = _logEntries.Count > 0 ? _logEntries.Last() : null;
                            try
                            {
                                int pid = int.Parse(GetReplacementString(entry, 0));
                                string direction = GetReplacementString(entry, 2) == @"%%14593" ? "Out" : "In";
                                string targetIp;
                                string targetPort;
                                if (direction == "Out")
                                {
                                    // outgoing target ip
                                    targetIp = GetReplacementString(entry, 5);
                                    targetPort = GetReplacementString(entry, 6);
                                }
                                else
                                {
                                    // incoming source ip
                                    targetIp = GetReplacementString(entry, 3);
                                    targetPort = GetReplacementString(entry, 4);
                                }

                                bool canBeIgnored = lastEntry != null
                                    && lastEntry.Pid == pid
                                    && lastEntry.Timestamp.Second == entry.TimeGenerated.Second
                                    && lastEntry.Timestamp.Minute == entry.TimeGenerated.Minute
                                    && lastEntry.TargetIP == targetIp
                                    && lastEntry.TargetPort == targetPort;

                                if (!canBeIgnored)
                                {
                                    string friendlyPath = GetReplacementString(entry, 1) == "-" ? "System" : FileHelper.GetFriendlyPath(GetReplacementString(entry, 1));
                                    string fileName = System.IO.Path.GetFileName(friendlyPath);
                                    int protocol = int.Parse(GetReplacementString(entry, 7));

                                    // try to get the servicename from pid (works only if service is running)
                                    string serviceName = services.ContainsKey(pid) ? services[pid].Name : "-";

                                    var le = new LogEntryViewModel()
                                    {
                                        Pid = pid,
                                        Timestamp = entry.TimeGenerated,
                                        Icon = IconHelper.GetIcon(GetReplacementString(entry, 1)),
                                        Path = GetReplacementString(entry, 1) == "-" ? "System" : GetReplacementString(entry, 1),
                                        FriendlyPath = friendlyPath,
                                        ServiceName = serviceName,
                                        FileName = fileName,
                                        TargetIP = targetIp,
                                        TargetPort = targetPort,
                                        Protocol = FirewallHelper.getProtocolAsString(protocol),
                                        Direction = direction,
                                        FilterId = GetReplacementString(entry, 8),
                                        Reason = FirewallHelper.getEventInstanceIdAsString(entry.InstanceId),
                                        Reason_Info = entry.Message,
                                    };
                                    le.ReasonColor = le.Reason.StartsWith("Block") ? Brushes.OrangeRed : Brushes.Blue;
                                    le.DirectionColor = le.Direction.StartsWith("In") ? Brushes.OrangeRed : Brushes.Black;
                                    _logEntries.Add(le);
                                    //if (le.Protocol == "TCP") {
                                    tcpIPList.Add(le.TargetIP);
                                    //}
                                    eventsStored++;
                                }
                            }
                            catch (Exception ex)
                            {
                                LogHelper.Error("Cannot parse eventlog entry: eventID=" + entry.InstanceId.ToString(), ex);
                            }
                        }
                    }

                    if (Settings.Default.EnableDnsResolver)
                    {
                        _ = DnsResolver.ResolveIpAddresses(tcpIPList);
                    }

                    ICollectionView dataView = CollectionViewSource.GetDefaultView(gridLog.ItemsSource);
                    if (dataView.SortDescriptions.Count < 1)
                    {
                        dataView.SortDescriptions.Add(new SortDescription("Timestamp", ListSortDirection.Descending));
                    }
                    if (dataView.Filter == null)
                    {
                        Predicate<Object> filter = (Object o) =>
                        {
                            LogEntryViewModel le = (LogEntryViewModel)o;
                            return IsTCPOnlyEnabled ? le.Protocol == "TCP" : true;
                        };
                        dataView.Filter += filter;
                    }

                    // Trim the list
                    while (_logEntries.Count > MaxEventsToLoad)
                    {
                        _logEntries.RemoveAt(0);
                    }

                    // Set the cut-off point for the next time this function gets called.
                    lastDate = lastDateNew;
                }
            }
            catch (Exception e)
            {
                LogHelper.Error("Unable to load the event log", e);
            }
        }

        private static string GetReplacementString(EventLogEntry entry, int i)
        {
            // check out of bounds
            if (i < entry.ReplacementStrings.Length)
            {
                return entry.ReplacementStrings[i];
            } else {
                return "";
            }
        }

        private void btnLocate_Click(object sender, RoutedEventArgs e)
        {
            var selectedLog = (LogEntryViewModel)gridLog.SelectedItem;
            if (selectedLog == null)
            {
                //@
                return;
            }
            Process.Start("explorer.exe", "/select," + selectedLog.FriendlyPath);
        }

        private void btnEventLogVwr_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("eventvwr.msc");
        }

        private void btnRestartAdmin_Click(object sender, RoutedEventArgs e)
        {
            ((App)Application.Current).RestartAsAdmin();
        }

        private void GridLog_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //System.Console.WriteLine($"Grid SelectionChanged: {sender}, {e.Source}, {e.Handled}, {e.OriginalSource}, {e}");
            if (gridLog.SelectedItem == null)
            {
                btnLocate.IsEnabled = false;
            }
            else
            {
                btnLocate.IsEnabled = true;
                if ((bool)btnAutoRefreshToggle.IsChecked)
                {
                    // disable the auto-refresh for not loosing the selection to locate
                    btnAutoRefreshToggle.IsChecked = false;
                }
            }
        }

        private void Reason_GotFocus(object sender, RoutedEventArgs e)
        {
            // System.Console.WriteLine($"Columng Reason GotFocus: {sender}, {e.Source}, {e.Handled}, {e.OriginalSource}, {e}");
        }

        private void GridLog_CellSelected(object sender, RoutedEventArgs e)
        {
            //System.Console.WriteLine($"CellSelected: {sender}, {e.Source}, {e.Handled}, {e.OriginalSource}, {e}");
            ShowMatchingRuleAndDetails((DataGrid)e.Source, (DataGridCell)e.OriginalSource);  // case when selection changed

        }
        private void GridLog_GotFocus(object sender, RoutedEventArgs e)
        {
            //System.Console.WriteLine($"Cell GotFocus: {sender}, {e.Source}, {e.Handled}, {e.OriginalSource}, {e}");
            ShowMatchingRuleAndDetails((DataGrid)e.Source, (DataGridCell)e.OriginalSource);  // case when row already selected and cell got focus
        }

        private void ShowMatchingRuleAndDetails(DataGrid grid, DataGridCell cell)
        {
            LogEntryViewModel selectedEntry = (LogEntryViewModel)grid.SelectedItem;
            if (selectedEntry != null && Reason.Equals(cell.Column) && cell.IsFocused && cell.IsSelected)
            {
                // Filter which blocked the connection
                string matchingFilterDetails;
                try
                {
                    FilterResult matchingFilter = NetshHelper.FindMatchingFilterInfo(int.Parse(selectedEntry.FilterId), refreshData: RefreshFilterData);
                    RefreshFilterData = false;
                    string filterInfo = WrapTextTrunc($"{ matchingFilter.Name} - { matchingFilter.Description}", 120, "\t");
                    matchingFilterDetails = matchingFilter != null ? $"\n\n" +
                        $"Filter rule which triggered the event:\n" +
                        $"\t{selectedEntry.FilterId}: {filterInfo}\n" : 
                        "\n\n... No filter rule found ...";
                } catch (Exception ex)
                {
                    LogHelper.Warning("Cannot get filter rule:" + ex.Message);
                    matchingFilterDetails = $"\n\n" +
                        $"Cannot get filter rule: {ex.Message}";
                }

                //// Other matching filters for process
                //IEnumerable<FirewallHelper.Rule> rules = FirewallHelper.GetMatchingRulesForEvent(int.Parse(selectedEntry.Pid), selectedEntry.Path, selectedEntry.TargetIP, selectedEntry.TargetPort, blockOnly: false, outgoingOnly: false);
                //string reasonDetails = $"\nMatching Rules for | {selectedEntry.FileName} | {selectedEntry.Pid} | {selectedEntry.TargetIP}:{selectedEntry.TargetPort} |";
                //foreach (FirewallHelper.Rule rule in rules.Take(10))
                //{
                //    reasonDetails += $"\n'{rule.Name}' | {rule.ActionStr} | {rule.DirectionStr} | {rule.AppPkgId} | profile={rule.ProfilesStr} | svc={rule.ServiceName} | {System.IO.Path.GetFileName(rule.ApplicationName)}";
                //}
                //if (rules.Count() > 10)
                //{
                //    reasonDetails += "\n...more...";
                //}
                //else if (rules.Count() == 0)
                //{
                //    reasonDetails += "\n... no matching rules found ...";
                //}
                string serviceNameInfo = !string.IsNullOrEmpty(selectedEntry.ServiceName) ? $"{selectedEntry.ServiceName}" : "-";
                ShowToolTip((Control)cell, selectedEntry.Reason_Info + $"\n\nService:\t{serviceNameInfo}" + matchingFilterDetails); // + reasonDetails);
            }
            else
            {
                CloseToolTip();
            }
        }

        private string WrapTextTrunc(string text, int maxChars, string indent = " ") { 
            if (text.Length > maxChars)
            {
                return Regex.Replace(text, "(.{" + maxChars + "})", "$1\n" + indent);
            }
            return text;
        }

        private static ToolTip toolTipInstance = new ToolTip
        {
            Content = "",
            PlacementTarget = null,
            StaysOpen = true,
            IsOpen = false
        };

        private void ShowToolTip(UIElement placementTarget, String text)
        {
            toolTipInstance.PlacementTarget = placementTarget;
            toolTipInstance.Content = text;
            toolTipInstance.IsOpen = true;
            placementTarget.LostFocus += PlacementTarget_LostFocus;
        }

        private static void CloseToolTip()
        {
            toolTipInstance.Content = "";
            toolTipInstance.IsOpen = false;
            toolTipInstance.PlacementTarget = null;
        }

        private void PlacementTarget_LostFocus(object sender, RoutedEventArgs e)
        {
            CloseToolTip();
            (sender as UIElement).LostFocus -= PlacementTarget_LostFocus;
        }

        private void ShowToolTip(Control control)
        {
            // shows the controls tooltip on demand
            if (control.ToolTip != null)
            {
                if (control.ToolTip is ToolTip castToolTip)
                {
                    castToolTip.IsOpen = true;
                }
                else
                {
                    _ = new ToolTip
                    {
                        Content = control.ToolTip,
                        StaysOpen = false,
                        IsOpen = true
                    };
                }
            }
        }
    }
}
