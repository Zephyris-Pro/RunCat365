// Copyright 2020 Takuto Nakamura
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.

using Microsoft.VisualBasic.Devices;
using Microsoft.Win32;
using RunCat365.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Resources;
using System.Windows.Forms;

namespace RunCat365
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // Terminate RunCat365 if there's any existing instance.
            var procMutex = new System.Threading.Mutex(true, "_RUNCAT_MUTEX", out var result);
            if (!result)
            {
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);

            try
            {
                Application.Run(new RunCat365ApplicationContext());
            }
            finally
            {
                procMutex?.ReleaseMutex();
            }
        }
    }

    public class DarkModeRenderer : ToolStripProfessionalRenderer
    {
        public DarkModeRenderer() : base(new DarkModeColorTable()) { 
        
        }


        // White arrow for dropdown items
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.White;
            base.OnRenderArrow(e);
        }
    }

    public class DarkModeColorTable : ProfessionalColorTable
    {
        public static readonly Color BaseColor = Color.FromArgb(43, 43, 43);
        public static readonly Color HoverColor = Color.FromArgb(53, 53, 53);
        public static readonly Color BorderColor = Color.Transparent;

        public override Color ToolStripDropDownBackground => BaseColor;
        public override Color ImageMarginGradientBegin => BaseColor;
        public override Color ImageMarginGradientMiddle => BaseColor;
        public override Color ImageMarginGradientEnd => BaseColor;
        public override Color MenuBorder => BorderColor;
        public override Color MenuItemBorder => BorderColor;
        public override Color MenuItemSelected => HoverColor;
        public override Color MenuItemSelectedGradientBegin => HoverColor;
        public override Color MenuItemSelectedGradientEnd => HoverColor;
        public override Color MenuItemPressedGradientBegin => HoverColor;
        public override Color MenuItemPressedGradientEnd => HoverColor;
    }

    public class RunCat365ApplicationContext : ApplicationContext
    {
        private const int CPU_TIMER_DEFAULT_INTERVAL = 5000;
        private const int ANIMATE_TIMER_DEFAULT_INTERVAL = 200;
        private PerformanceCounter cpuUsage;
        private PerformanceCounter ramUsage;
        private PerformanceCounter diskFree;
        private ToolStripMenuItem runnerMenu;
        private ToolStripMenuItem themeMenu;
        private ToolStripMenuItem startupMenu;
        private ToolStripMenuItem fpsMaxLimitMenu;
        private NotifyIcon notifyIcon;
        private Runner runner = Runner.Cat;
        private Theme manualTheme = Theme.System;
        private FPSMaxLimit fpsMaxLimit = FPSMaxLimit.FPS40;
        private int current = 0;
        private float interval;
        private Icon[] icons;
        private Timer animateTimer = new Timer();
        private Timer cpuTimer = new Timer();

        public RunCat365ApplicationContext()
        {
            UserSettings.Default.Reload();
            Enum.TryParse(UserSettings.Default.Runner, out runner);
            Enum.TryParse(UserSettings.Default.Theme, out manualTheme);
            Enum.TryParse(UserSettings.Default.FPSMaxLimit, out fpsMaxLimit);

            Application.ApplicationExit += new EventHandler(OnApplicationExit);

            SystemEvents.UserPreferenceChanged += new UserPreferenceChangedEventHandler(UserPreferenceChanged);

            cpuUsage = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");
            _ = cpuUsage.NextValue(); // discards first return value

            ramUsage = new PerformanceCounter("Memory", "Available MBytes");
            _ = ramUsage.NextValue();


            // TODO Check if there's multiple drive letters

            // Actual driveLetter
            string driveLetter = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\');
            diskFree = new PerformanceCounter("LogicalDisk", "% Free Space", driveLetter);
            _ = diskFree.NextValue();

            var items = new List<ToolStripMenuItem>();
            foreach (Runner r in Enum.GetValues(typeof(Runner)))
            {
                var item = new ToolStripMenuItem(r.GetString(), null, SetRunner)
                {
                    Checked = runner == r
                };
                items.Add(item);
            }
            runnerMenu = new ToolStripMenuItem("Runner", null, items.ToArray());

            items.Clear();
            foreach (Theme t in Enum.GetValues(typeof(Theme)))
            {
                var item = new ToolStripMenuItem(t.GetString(), null, SetThemeIcons)
                {
                    Checked = manualTheme == t
                };
                items.Add(item);
            }
            themeMenu = new ToolStripMenuItem("Theme", null, items.ToArray());

            items.Clear();
            foreach (FPSMaxLimit f in Enum.GetValues(typeof(FPSMaxLimit)))
            {
                var item = new ToolStripMenuItem(f.GetString(), null, SetFPSMaxLimit)
                {
                    Checked = fpsMaxLimit == f
                };
                items.Add(item);
            }
            fpsMaxLimitMenu = new ToolStripMenuItem("FPS Max Limit", null, items.ToArray());

            startupMenu = new ToolStripMenuItem("Startup", null, SetStartup);
            if (IsStartupEnabled())
            {
                startupMenu.Checked = true;
            }

            string appVersion = $"{Application.ProductName} v{Application.ProductVersion}";
            ToolStripMenuItem appVersionMenu = new ToolStripMenuItem(appVersion)
            {
                Enabled = false
            };

            ContextMenuStrip contextMenuStrip = new ContextMenuStrip(new Container());
            contextMenuStrip.Items.AddRange(
            
                runnerMenu,
                themeMenu,
                fpsMaxLimitMenu,
                startupMenu,
                new ToolStripSeparator(),
                appVersionMenu,
                new ToolStripMenuItem("Exit", null, Exit)
            );


            bool isSystemDark = GetSystemTheme() == Theme.Dark;
            bool enableDarkMode = manualTheme == Theme.Dark || (manualTheme == Theme.System && isSystemDark);

            ApplyDarkTheme(contextMenuStrip, enableDarkMode);


            SetIcons();

            notifyIcon = new NotifyIcon()
            {
                Icon = icons[0],
                ContextMenuStrip = contextMenuStrip,
                Text = "Loading...",
                Visible = true
            };

            notifyIcon.DoubleClick += new EventHandler(HandleDoubleClick);

            SetAnimation();
            StartObserveCPU();

            current = 1;
        }

        private void OnApplicationExit(object sender, EventArgs e)
        {
            UserSettings.Default.Runner = runner.ToString();
            UserSettings.Default.Theme = manualTheme.ToString();
            UserSettings.Default.FPSMaxLimit = fpsMaxLimit.ToString();
            UserSettings.Default.Save();
        }

        private bool IsStartupEnabled()
        {
            string keyName = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using (RegistryKey rKey = Registry.CurrentUser.OpenSubKey(keyName))
            {
                return (rKey.GetValue(Application.ProductName) != null) ? true : false;
            }
        }

        private Theme GetSystemTheme()
        {
            string keyName = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            using (RegistryKey rKey = Registry.CurrentUser.OpenSubKey(keyName))
            {
                object value;
                if (rKey == null || (value = rKey.GetValue("SystemUsesLightTheme")) == null)
                {
                    Console.WriteLine("Oh No! Couldn't get theme light/dark");
                    return Theme.Light;
                }
                return (int)value == 0 ? Theme.Dark : Theme.Light;
            }
        }

        private void SetIcons()
        {
            Theme systemTheme = GetSystemTheme();
            string prefix = (manualTheme == Theme.System ? systemTheme : manualTheme).GetString();
            string runnerName = runner.GetString();
            ResourceManager rm = Resources.ResourceManager;
            int capacity = runner.GetFrameNumber();
            List<Icon> list = new List<Icon>(capacity);
            for (int i = 0; i < capacity; i++)
            {
                string iconName = $"{prefix}_{runnerName}_{i}".ToLower();
                list.Add((Icon)rm.GetObject(iconName));
            }
            icons = list.ToArray();
        }

        private void UpdateCheckedState(ToolStripMenuItem sender, ToolStripMenuItem menu)
        {
            foreach (ToolStripMenuItem item in menu.DropDownItems)
            {
                item.Checked = false;
            }
            sender.Checked = true;
        }

        private void SetRunner(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            UpdateCheckedState(item, runnerMenu);
            Enum.TryParse(item.Text, out runner);
            SetIcons();
        }

        private void SetThemeIcons(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            UpdateCheckedState(item, themeMenu);
            Enum.TryParse(item.Text, out manualTheme);
            SetIcons();

            bool isSystemDark = GetSystemTheme() == Theme.Dark;
            bool enableDarkMode = manualTheme == Theme.Dark || (manualTheme == Theme.System && isSystemDark);

            ApplyDarkTheme(notifyIcon.ContextMenuStrip, enableDarkMode);

        }

        private void SetFPSMaxLimit(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            UpdateCheckedState(item, fpsMaxLimitMenu);
            fpsMaxLimit = _FPSMaxLimit.Parse(item.Text);
        }

        private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General) SetIcons();
        }

        private void SetStartup(object sender, EventArgs e)
        {
            startupMenu.Checked = !startupMenu.Checked;
            string keyName = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using (RegistryKey rKey = Registry.CurrentUser.OpenSubKey(keyName, true))
            {
                if (startupMenu.Checked)
                {
                    rKey.SetValue(Application.ProductName, Process.GetCurrentProcess().MainModule.FileName);
                }
                else
                {
                    rKey.DeleteValue(Application.ProductName, false);
                }
                rKey.Close();
            }
        }

        private void Exit(object sender, EventArgs e)
        {
            cpuUsage.Close();
            animateTimer.Stop();
            cpuTimer.Stop();
            notifyIcon.Visible = false;
            Application.Exit();
        }

        private void AnimationTick(object sender, EventArgs e)
        {
            if (icons.Length <= current) current = 0;
            notifyIcon.Icon = icons[current];
            current = (current + 1) % icons.Length;
        }

        private void SetAnimation()
        {
            animateTimer.Interval = ANIMATE_TIMER_DEFAULT_INTERVAL;
            animateTimer.Tick += new EventHandler(AnimationTick);
            animateTimer.Start();
        }

        // Since there's now multiple informations (CPU, RAM, Disk ...) we probably need to rename this method.
        private void CPUTick()
        {
            // Range of CPU percentage: 0-100 (%)
            float cpuPercentage = Math.Min(100, cpuUsage.NextValue());

            float ramPercentage = GetMemoryUsagePercentage();

            float diskPercentage = 100f - Math.Min(100, diskFree.NextValue());

            notifyIcon.Text = $"CPU: {cpuPercentage:f1}%\nRAM: {ramPercentage:f1}%\nStorage: {diskPercentage:f1}% used";

            // Range of interval: 25-500 (ms) = 2-40 (fps)
            interval = 500.0f / (float)Math.Max(1.0f, (cpuPercentage / 5.0f) * fpsMaxLimit.GetRate());

            animateTimer.Stop();
            animateTimer.Interval = (int)interval;
            animateTimer.Start();
        }

        private void ObserveCPUTick(object sender, EventArgs e)
        {
            CPUTick();
        }

        private void StartObserveCPU()
        {
            cpuTimer.Interval = CPU_TIMER_DEFAULT_INTERVAL;
            cpuTimer.Tick += new EventHandler(ObserveCPUTick);
            cpuTimer.Start();
        }


        // This is probably triggering AV checks.
        //private void HandleDoubleClick(object Sender, EventArgs e)
        //{
        //    var startInfo = new ProcessStartInfo
        //    {
        //        FileName = "powershell",
        //        UseShellExecute = false,
        //        Arguments = " -c Start-Process taskmgr.exe",
        //        CreateNoWindow = true,
        //    };
        //    Process.Start(startInfo);
        //}


        // This is much less suspicious to AVs and work's across Windows 7�11.
        private void HandleDoubleClick(object sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "taskmgr.exe",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open Task Manager: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private float GetMemoryUsagePercentage()
        {
            float availableMb = ramUsage.NextValue();
            float totalMb = GetTotalMemoryInMBytes();
            float usedMb = totalMb - availableMb;
            return (usedMb / totalMb) * 100.0f;
        }

        private float GetTotalMemoryInMBytes()
        {
            return new ComputerInfo().TotalPhysicalMemory / (1024f * 1024f);
        }
        private void ApplyDarkColorsToMenuItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                item.ForeColor = Color.White;
                item.BackColor = DarkModeColorTable.BaseColor;

                if (item is ToolStripMenuItem menuItem && menuItem.DropDownItems.Count > 0)
                {
                    ApplyDarkColorsToMenuItems(menuItem.DropDownItems);
                }
            }
        }
        private void ResetItemsColors(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                item.ForeColor = SystemColors.ControlText;
                item.BackColor = SystemColors.Control;

                if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
                {
                    menuItem.DropDown.Renderer = new ToolStripProfessionalRenderer();
                    ResetItemsColors(menuItem.DropDownItems);
                }
            }
        }

        private void ApplyDarkTheme(ContextMenuStrip menu, bool enabled)
        {
            if (enabled)
            {
                menu.Renderer = new DarkModeRenderer();
                ApplyDarkColorsToMenuItems(menu.Items);
            }
            else
            {
                menu.Renderer = new ToolStripProfessionalRenderer();
                ResetItemsColors(menu.Items);
            }
        }
    }
}
