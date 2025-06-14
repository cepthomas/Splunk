using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using System.Text;
using System.Reflection;
using System.Text.Json;
using System.Security.Policy;
using System.Runtime.InteropServices;
using Ephemera.NBagOfTricks;
using Ephemera.NBagOfUis;
using WI = Ephemera.Win32.Internals;
using WM = Ephemera.Win32.WindowManagement;

// TODO install procedure, creates settings commands and writes the registry.

namespace Splunk.Ui
{
    public partial class MainForm : Form
    {
        #region Fields
        /// <summary>My logger.</summary>
        readonly Logger _logger = LogManager.CreateLogger("Splunk.Ui");

        /// <summary>App settings.</summary>
        readonly UserSettings _settings;

        /// <summary>Measure performance.</summary>
        readonly Stopwatch _sw = new();

        /// <summary>Hook message processing.</summary>
        readonly int _hookMsg;

        /// <summary>All the commands.</summary>
        List<ExplorerCommand> _commands =
        [
            new("cmder", ExplorerContext.Dir, "Commander", "%SPLUNK %ID \"%D\"", "Open a new explorer next to the current."),
            new("tree", ExplorerContext.Dir, "Tree", "%SPLUNK %ID \"%D\"", "Copy a tree of selected directory to clipboard"),
            new("openst", ExplorerContext.Dir, "Open in Sublime", "\"%ProgramFiles%\\Sublime Text\\subl\" --launch-or-new-window \"%D\"", "Open selected directory in Sublime Text."),
            new("findev", ExplorerContext.Dir, "Find in Everything", "%ProgramFiles%\\Everything\\everything -parent \"%D\"", "Open selected directory in Everything."),
            new("tree", ExplorerContext.DirBg, "Tree", "%SPLUNK %ID \"%W\"", "Copy a tree here to clipboard."),
            new("openst", ExplorerContext.DirBg, "Open in Sublime", "\"%ProgramFiles%\\Sublime Text\\subl\" --launch-or-new-window \"%W\"", "Open here in Sublime Text."),
            new("findev", ExplorerContext.DirBg, "Find in Everything", "%ProgramFiles%\\Everything\\everything -parent \"%W\"", "Open here in Everything."),
            new("exec", ExplorerContext.File, "Execute", "%SPLUNK %ID \"%D\"", "Execute file if executable otherwise opened."),
            new("test_deskbg", ExplorerContext.DeskBg, "!! Test DeskBg", "%SPLUNK %ID \"%W\"", "Debug stuff."),
            new("test_folder", ExplorerContext.Folder, "!! Test Folder", "%SPLUNK %ID \"%D\"", "Debug stuff."),
        ];
        #endregion

        #region Lifecycle
        /// <summary>Constructor.</summary>
        public MainForm()
        {
            //_sw.Start();

            // Must do this first before initializing.
            string appDir = MiscUtils.GetAppDataDir("Splunk", "Ephemera");
            _settings = (UserSettings)SettingsCore.Load(appDir, typeof(UserSettings));

            InitializeComponent();

            // Init logging.
            LogManager.MinLevelFile = _settings.FileLogLevel;
            LogManager.MinLevelNotif = _settings.NotifLogLevel;
            LogManager.LogMessage += (_, e) => { tvInfo.AppendLine($"{e.Message}"); };
            LogManager.Run($"{appDir}\\log.txt", 100000);

            // Main form.
            StartPosition = FormStartPosition.Manual;
            Location = _settings.FormGeometry.Location;
            Size = _settings.FormGeometry.Size;
            WindowState = FormWindowState.Normal;
            // Gets the icon associated with the currently executing assembly.
            Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);

            // Info display.
            tvInfo.MatchText.Add("ERR", Color.LightPink);
            tvInfo.BackColor = Color.Cornsilk;
            tvInfo.Prompt = ">";

            // Misc ui clickers.
            btnEdit.Click += (sender, e) => { SettingsEditor.Edit(_settings, "User Settings", 500); };
            btnDump.Click += (sender, e) => { WM.GetAppWindows("explorer").ForEach(w => tvInfo.AppendLine(w.ToString())); };

            // Manage commands in registry.
            btnInitReg.Click += (sender, e) => { _commands.ForEach(c => c.CreateRegistryEntry(Path.Join(Environment.CurrentDirectory, "Splunk.exe"))); };
            btnClearReg.Click += (sender, e) => { _commands.ForEach(c => c.RemoveRegistryEntry()); };

            // Shell handlers.
            _hookMsg = WI.RegisterShellHook(Handle); // test for 0?
            WI.RegisterHotKey(Handle, (int)Keys.A, WI.MOD_ALT | WI.MOD_CTRL);

            // Debug stuff.
            btnGo.Click += (sender, e) => { DoStuff(); };
        }

        /// <summary>
        /// Clean up on shutdown.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            LogManager.Stop();

            // Save user settings.
            _settings.FormGeometry = new()
            {
                X = Location.X,
                Y = Location.Y,
                Width = Width,
                Height = Height
            };

            _settings.Save();

            base.OnFormClosing(e);
        }

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                WI.DeregisterShellHook(Handle);
                WI.UnregisterHotKeys(Handle);

                components?.Dispose();
            }
            base.Dispose(disposing);
        }
        #endregion

        #region Windows hooks
        /// <summary>
        /// Handle the hooked shell messages: shell window lifetime and hotkeys.
        /// </summary>
        /// <param name="message"></param>
        protected override void WndProc(ref Message message)
        {
            IntPtr handle = message.LParam;
            if (message.Msg == _hookMsg)
            {
                var shellEvent = message.WParam.ToInt32();

                switch (shellEvent)
                {
                    case WI.HSHELL_WINDOWCREATED:
                        WM.AppWindowInfo wi = WM.GetAppWindowInfo(handle);
                        _logger.Debug($"WindowCreatedEvent:{handle} {wi.Title}");
                        break;

                    case WI.HSHELL_WINDOWDESTROYED:
                        _logger.Debug($"WindowDestroyedEvent:{handle}");
                        break;
                }
            }
            else if (message.Msg == WI.WM_HOTKEY_MESSAGE_ID) // Decode key.
            {
                Keys key = Keys.None;
                int mod = (int)((long)message.LParam & 0xFFFF);
                if (Enum.IsDefined(typeof(Keys), message.LParam >> 16))
                {
                    key = (Keys)Enum.ToObject(typeof(Keys), message.LParam >> 16);
                }
                // else do something?

                if ((key != Keys.None) && (mod & WI.MOD_ALT) > 0 && (mod & WI.MOD_CTRL) > 0)
                {
                    _logger.Debug($"Hotkey:{key}");
                    //switch (key) etc...
                }
            }

            base.WndProc(ref message);
        }
        #endregion

        #region Debug
        /// <summary>
        /// Debug stuff.
        /// </summary>
        void DoStuff()
        {
            //CreateCommands();
            //return;

            List<string> args = ["tree", @"C:\Users\cepth\AppData\Roaming\Sublime Text\Packages"];
            //List<string> args = ["cmder", @"C:\Users\cepth\AppData\Roaming\Sublime Text\Packages"];
            //List<string> args = ["exec", @"C:\Dev\repos\Apps\Splunk\Test\go.cmd"];
            //List<string> args = ["exec", @"C:\Dev\repos\Apps\Splunk\Test\go.lua"];
            //List<string> args = ["test_deskbg", @"C:\Dev\repos\Apps\Splunk\Test\dummy.txt"];

            try
            {
                Splunk.Program.Run(args);
            }
            catch (Exception ex)
            {
                tvInfo.AppendLine($"ERR {ex.Message}");
                Debug.WriteLine(ex.ToString());
            }
        }
        #endregion
    }
}
