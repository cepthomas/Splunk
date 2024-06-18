using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using Ephemera.NBagOfTricks;
using System.Linq;
using System.Drawing;
using System.ComponentModel;
using System.Text;
using System.Runtime.InteropServices;
//using Splunk.Common;

//using NM = Splunk.Common.NativeMethods;
//using SU = Splunk.Common.ShellUtils;

using WI = Win32BagOfTricks.Internals;
using WM = Win32BagOfTricks.WindowManagement;
using CB = Win32BagOfTricks.Clipboard;


//TODO1 move bins to a convenient location for reg to find.

namespace Splunk
{
    public class Program
    {
        #region Fields - public for debugging
        /// <summary>Result of command execution.</summary>
        public static string _stdout = "";

        /// <summary>Result of command execution.</summary>
        public static string _stderr = "";

        /// <summary>Log file name.</summary>
        static readonly string _logFileName = Path.Join(MiscUtils.GetAppDataDir("Splunk", "Ephemera"), "splunk.txt");
        #endregion

        /// <summary>Where it all began.</summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            Log($"Splunk cl args:{string.Join(" ", args)}");

            // I'm in charge of the pixels.
            WI.DisableDpiScaling();

            Stopwatch sw = new();
            sw.Start();

            // Execute. Run throws various exceptions depending on the origin of the error.
            try
            {
                Run([.. args]);

                Environment.ExitCode = 0;
                Log("Everything went OK");
                CB.SetText(_stdout);
            }
            catch (SplunkException ex)
            {
                if (ex.IsError)
                {
                    Environment.ExitCode = 1;
                    Log($"Splunk ERROR: {ex.Message}");
                    CB.SetText($"{ex.Message}{Environment.NewLine}{ex.StackTrace}");
                    WI.MessageBox(ex.Message, "See the clipboard", true);
                }
                else // just notify
                {
                    Log($"Splunk INFO: {ex.Message}");
                    Environment.ExitCode = 0;
                    WI.MessageBox(ex.Message, "You should know");
                }
            }
            catch (Win32Exception ex)
            {
                Log($"Spawned process ERROR: {ex.ErrorCode} {ex.Message}");
                CB.SetText($"{ex.Message}{Environment.NewLine}{_stderr}");
                Environment.ExitCode = 2;
                WI.MessageBox(ex.Message, "See the clipboard", true);
            }
            catch (Exception ex) // something else
            {
                Log($"Internal ERROR: {ex.Message}");
                CB.SetText($"{ex.Message}{Environment.NewLine}{ex.StackTrace}");
                Environment.ExitCode = 3;
                WI.MessageBox(ex.Message, "See the clipboard", true);
            }

            sw.Stop();
            Log($"Exit code:{Environment.ExitCode} msec:{sw.ElapsedMilliseconds}");

            // Before we end, manage log file.
            FileInfo fi = new(_logFileName);
            if (fi.Exists && fi.Length > 10000)
            {
                var lines = File.ReadAllLines(_logFileName);
                int start = lines.Length / 3;
                var trunc = lines.Subset(start, lines.Length - start);
                File.WriteAllLines(_logFileName, trunc);
                //Log($">>>> Trimmed log file");
            }
        }

        /// <summary>Do the work.</summary>
        /// <param name="args"></param>
        public static void Run(List<string> args)
        {
            // Process the args => Splunk.exe id path
            if (args.Count != 2) { throw new SplunkException($"Invalid command line format", true); }
            var id = Environment.ExpandEnvironmentVariables(args[0]);
            var path = Environment.ExpandEnvironmentVariables(args[1]);

            // Check for valid path.
            if (path.StartsWith("::")) { throw new SplunkException($"Can't use magic system folders e.g. Home", false); }
            else if (!Path.Exists(path)) { throw new SplunkException($"Invalid path [{path}]", true); }

            // Final details.
            FileAttributes attr = File.GetAttributes(path);
            var wdir = attr.HasFlag(FileAttributes.Directory) ? path : Path.GetDirectoryName(path)!;
            var isdir = attr.HasFlag(FileAttributes.Directory);

            switch (id)
            {
                case "cmder":
                    var fgHandle = WM.ForegroundWindow; // -> left pane
                    WM.AppWindowInfo fginfo = WM.GetAppWindowInfo(fgHandle);

                    // New explorer -> right pane.
                    WI.ShellExecute("explore", path);

                    // Locate the new explorer window. Wait for it to be created. This is a bit klunky but there does not appear to be a more direct method.
                    int tries = 0; // ~4
                    WM.AppWindowInfo? rightPane = null;
                    for (tries = 0; tries < 20 && rightPane is null; tries++)
                    {
                        System.Threading.Thread.Sleep(50);
                        var wins = WM.GetAppWindows("explorer");
                        rightPane = wins.Where(w => w.Title == path).FirstOrDefault();
                    }
                    if (rightPane is null) throw new SplunkException($"Couldn't create right pane for [{path}]", true);

                    // Relocate/resize the windows to fit available real estate.
                    WM.AppWindowInfo desktop = WM.GetAppWindowInfo(WM.ShellWindow);
                    int w = desktop.DisplayRectangle.Width * 45 / 100;
                    int h = desktop.DisplayRectangle.Height * 80 / 100;
                    int t = 50, l = 50;
                    WM.MoveWindow(fgHandle, l, t, w, h);
                    WM.ForegroundWindow = fgHandle;
                    l += w;
                    WM.MoveWindow(rightPane.Handle, l, t, w, h);
                    WM.ForegroundWindow = rightPane.Handle;
                    break;

                case "tree":
                    {
                        int code = ExecuteCommand("cmd", wdir, $"/c tree /a /f \"{wdir}\" | clip");
                        if (code != 0) { throw new Win32Exception(code); }
                    }
                    break;

                case "exec":
                    if (!isdir)
                    {
                        var ext = Path.GetExtension(path);
                        int code = ext switch
                        {
                            ".cmd" or ".bat" => ExecuteCommand("cmd", wdir, $"/c \"{path}\""),
                            ".ps1" => ExecuteCommand("powershell", wdir, $"-executionpolicy bypass -File \"{path}\""),
                            ".lua" => ExecuteCommand("lua", wdir, $"\"{path}\""),
                            ".py" => ExecuteCommand("python", wdir, $"\"{path}\""),
                            _ => ExecuteCommand("cmd", wdir, $"/c \"{path}\"") // default just open.
                        };
                        if (code != 0) { throw new Win32Exception(code); }
                    }
                    else
                    {
                        // ignore selection of dir
                    }
                    break;

                case "test_deskbg":
                    Log($"!!! Got test_deskbg");
                    WI.MessageBox($"!!! Got test_deskbg: {path}", "Debug");
                    break;

                case "test_folder":
                    Log($"!!! Got test_folder");
                    WI.MessageBox("!!! Got test_folder: {path}", "Debug");
                    break;

                default:
                    throw new SplunkException($"Invalid id: {id}", true);
            }
        }

        /// <summary>
        /// Generic command executor with hidden console.
        /// </summary>
        /// <param name="exe"></param>
        /// <param name="wdir"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        static int ExecuteCommand(string exe, string wdir, string args)
        {
            //Log($"DEBUG args:{args}");

            ProcessStartInfo pinfo = new()
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = wdir, // needed?
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                //RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using Process proc = new() { StartInfo = pinfo };
            proc.Start();
            proc.WaitForExit();
            // Save process results.
            _stdout = proc.StandardOutput.ReadToEnd();
            _stderr = proc.StandardError.ReadToEnd();

            return proc.ExitCode;
        }

        /// <summary>Simple logging, don't need or want a full-blown logger.</summary>
        static void Log(string msg)
        {
            if (_logFileName is not null)
            {
                File.AppendAllText(_logFileName, $"{DateTime.Now:yyyy'-'MM'-'dd HH':'mm':'ss.fff} {msg}{Environment.NewLine}");
            }
        }
    }

    /// <summary>App exception.</summary>
    /// <param name="msg"></param>
    /// <param name="isError">Otherwise info</param>
    class SplunkException(string msg, bool isError) : Exception(msg)
    {
        public bool IsError { get; } = isError;
    }
}
