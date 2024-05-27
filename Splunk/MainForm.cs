using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using Ephemera.NBagOfTricks;
using Ipc = Ephemera.NBagOfTricks.SimpleIpc;
using Com = Splunk.Common.Common;



namespace Splunk
{
    public partial class MainForm : Form
    {
        /// <summary>The boilerplate.</summary>
        readonly Ipc.Server _server;

        /// <summary>The multiprocess log.</summary>
        readonly Ipc.MpLog _log;

        record struct RegCommand(string RegPath, string Command, string Name, string Tag);

        readonly RegCommand[] _regCommands =
            [
            new("Directory", "cmder", "Two Pane", "dir"),
            new("Directory", "tree", "Tree", "dir"),
            new("Directory", "openst", "Open in Sublime", "dir"),
            new("Directory", "find", "Open in Everything", "dir"),
            new("Directory", "newtab", "Open in New Tab", "dir"),
            new("Directory\\Background", "tree", "Tree", "dirbg"),
            new("Directory\\Background", "openst", "Open in Sublime", "dirbg"),
            new("Directory\\Background", "find", "Open in Everything", "dirbg"),
            new("DesktopBackground", "newtab", "Open in New Tab", "deskbg"),
            ];


        string _message = ""; // TODO1 remove this? A bit klunky.


        #region Lifecycle
        /// <summary>
        ///Constructor.
        /// </summary>
        public MainForm()
        {
            InitializeComponent();

            //CheckForIllegalCrossThreadCalls = true;

            tvInfo.AppendLine($"MainForm() {Environment.CurrentManagedThreadId}");

            _log = new(Com.LogFileName, "SPLUNK");
            _log.Write("Hello from UI");

            // Info display.
            tvInfo.MatchColors.Add("ERROR ", Color.LightPink);
            tvInfo.BackColor = Color.Cornsilk;
            tvInfo.Prompt = ">";

            btnGo.Click += (_, __) => { Go(); };

            // Run server.
            _server = new(Com.PIPE_NAME, Com.LogFileName);
            _server.IpcReceive += Server_IpcReceive;
            _server.Start();
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="e"></param>
        protected override void OnLoad(EventArgs e)
        {
            // Current bin dir. C:\Dev\repos\Apps\Splunk\Splunk\bin\Debug\net8.0-windows
            // ==== CreateRegistryEntries(Environment.CurrentDirectory);

            base.OnLoad(e);
        }

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _server.Dispose();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }
        #endregion


        // --- temp debug stuff ---
        private void Go()
        {
            tvInfo.AppendLine($"Go() {Environment.CurrentManagedThreadId}");
            int _which = 1;

            if (_which == 0)
            {
                ProcessStartInfo startInfo = new()
                {
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = "cmd",
                    Arguments = $"/c echo Oscar {DateTime.Now.Millisecond} XYZ | clip",
                };

                Process process = new() { StartInfo = startInfo };
                process.Start();

                //process.WaitForExit(1000);
                // There is a fundamental difference when you call WaitForExit() without a time -out, it ensures that the redirected
                // stdout/ err have returned EOF.This makes sure that you've read all the output that was produced by the process.
                // We can't see what "onOutput" does, but high odds that it deadlocks your program because it does something nasty
                // like assuming that your main thread is idle when it is actually stuck in WaitForExit().
            }
            else
            {
                // Fake message.
                _message = "\"cmder\" \"dir\" \"C:\\Dev\\repos\\Apps\\Splunk\\Splunk\\obj\"";
                ProcessMessage();
            }

        }
        // --- temp debug stuff ---






        /// <summary>
        /// Client has something to say.
        /// </summary>
        /// <param name="_"></param>
        /// <param name="e"></param>
        void Server_IpcReceive(object? _, Ipc.IpcReceiveEventArgs e)
        {
            tvInfo.AppendLine($"Server_IpcReceive() {Environment.CurrentManagedThreadId}");

            if (e.Error)
            {
                _message = "";
                tvInfo.AppendLine("ERROR " + e.Message);
                _log.Write("ERROR " + e.Message);
            }
            else
            {
                _message = e.Message;
                tvInfo.AppendLine("RCV " + e.Message);
                BeginInvoke(ProcessMessage);
            }
        }

        /// <summary>
        /// Do the work.
        /// </summary>
        /// <param name="_"></param>
        /// <param name="e"></param>
        void ProcessMessage()
        {
            tvInfo.AppendLine($"ProcessMessage() {Environment.CurrentManagedThreadId}");

            try
            {
                string cmd;
                string path;
                string tag;
                string dir;

                // Process the command string. Should be like "command" "args".
                // Split and remove spaces.

                var parts = StringUtils.SplitByToken(_message, "\"");
                parts.RemoveAll(string.IsNullOrWhiteSpace);
                if (parts.Count != 3) { throw new($"invalid command format"); }
                cmd = parts[0];
                tag = parts[1];
                path = parts[2];

                // Check for valid path arg.
                if (!Path.Exists(path)) { throw new($"invalid path: {path}"); }
                FileAttributes attr = File.GetAttributes(path);
                dir = attr.HasFlag(FileAttributes.Directory) ? path : Path.GetDirectoryName(path)!;

                // Check for valid command and execute it.
                ProcessStartInfo pinfo = new()
                {
                    UseShellExecute = false, //true,
                    CreateNoWindow = true,
                    WorkingDirectory = dir,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    //RedirectStandardInput = true,
                    //RedirectStandardOutput = true,
                };

                switch (cmd)
                {
                    case "cmder":
                        // Open a new explorer window at the dir selected in the first one.
                        // Locate it on one side or other of the first, same size.
                        // TODO option for full screen?
                        pinfo.FileName = "explorer";
                        pinfo.Arguments = $"{dir}";
                        break;

                    case "newtab":
                        // Open a new explorer tab in current window at the dir selected in the first one.
                        // Something like https://github.com/tariibaba/WinENFET/blob/main/src (autohotkey)./win-e.ahk
                        pinfo.FileName = "cmd";
                        pinfo.Arguments = $"/c echo !!newtab!! {DateTime.Now.Millisecond} | clip";
                        break;

                    case "tree": // direct => cmd /c tree /a /f "%V" | clip
                        // still flashes, ? Try ShellExecuteEx, setting nShow=SW_HIDE.
                        pinfo.FileName = "cmd";
                        pinfo.Arguments = $"/C tree \"{dir}\" /a /f | clip";
                        break;

                    case "openst": // direct
                        pinfo.FileName = "subl";
                        pinfo.Arguments = $"-n \"{dir}\"";
                        break;

                    case "find": // direct
                        pinfo.FileName = "everything";
                        pinfo.Arguments = $"-parent \"{dir}\"";
                        pinfo.WorkingDirectory = @"C:\Program Files\Everything";
                        break;

                    default:
                        throw new($"command verb: {cmd}");
                }


                //_proc.StartInfo = pinfo;
                //_proc.Start();
                //_proc.WaitForExit();
                //_proc.Close();

                var proc = new Process() { StartInfo = pinfo };
                proc.Start();
                //proc.WaitForExit();
                //if (proc.ExitCode != 0) { throw new($"process exit code: {proc.ExitCode}"); }
            }
            catch (Exception ex) // handle errors
            {
                tvInfo.AppendLine("ERROR " + ex.Message);
                _log.Write("ERROR " + ex.Message);
            }
        }

        #region Manipulate registry
        /// <summary>
        /// 
        /// </summary>
        void RemoveRegistryEntries() //TODO1
        {
            //public void DeleteSubKeyTree(string subkey);

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientPath"></param>
        void CreateRegistryEntries(string clientPath)
        {
            using var hkcu = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Registry64);
            using var splunk_root = hkcu.OpenSubKey(@"Software\Classes", writable: true);
            foreach (var rc in _regCommands)
            {
                var strsubkey = $"{rc.RegPath}\\shell\\{rc.Command}";

                using (var k = splunk_root!.CreateSubKey(strsubkey))
                {
                    Debug.WriteLine($"MUIVerb={rc.Name}");
                    k.SetValue("MUIVerb", rc.Name);
                }

                strsubkey += "\\command";

                using (var k = splunk_root!.CreateSubKey(strsubkey))
                {
                    //cmd.exe /s /k pushd "%V"
                    //"C:\Program Files (x86)\Common Files\Microsoft Shared\MSEnv\VSLauncher.exe" "%1" source:Explorer

                    var scmd = $"\"{clientPath}\\SplunkClient.exe\" {rc.Command} {rc.Tag} \"%V\"";
                    Debug.WriteLine($"@={scmd}");
                    k.SetValue("", scmd);
                }
            }
        }
        #endregion
    }
}
