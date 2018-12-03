using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StampyCommon;

namespace StampyWorker.Jobs
{
    internal static class ProcessInvoker
    {
        private static ConcurrentDictionary<Guid, Process> _runningProcesses;
        public static Task Start(List<ProcessAction> actions, Action<string, bool> outputDelegate, CancellationToken? cancelToken)
        {
            foreach (var action in actions)
            {
                Start(action, outputDelegate, cancelToken);
            }

            return null;
        }

        public static Task Start(ProcessAction action, Action<string, bool> outputDelegate, CancellationToken? cancelToken = null)
        {
            if (string.IsNullOrWhiteSpace(action.ProgramPath))
            {
                throw new NullReferenceException($"One of the action arguments is null or empty");
            }

            if (!File.Exists(action.ProgramPath))
            {
                throw new FileNotFoundException($"Cannot find {action.ProgramPath}");
            }

            if (_runningProcesses == null)
            {
                _runningProcesses = new ConcurrentDictionary<Guid, Process>();
            }

            var processStartInfo = new ProcessStartInfo();
            processStartInfo.FileName = action.ProgramPath;
            processStartInfo.Arguments = action.Arguments != null ? string.Join(" ", action.Arguments.Select(a => a.ToString())) : null;
            processStartInfo.UseShellExecute = false;
            processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processStartInfo.RedirectStandardError = true;
            processStartInfo.RedirectStandardOutput = true;

            using (var p = Process.Start(processStartInfo))
            {
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
                p.OutputDataReceived += new DataReceivedEventHandler((sender, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) { outputDelegate(e.Data, false); } });
                p.ErrorDataReceived += new DataReceivedEventHandler((sender, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) { outputDelegate(e.Data, true); } });

                if (!_runningProcesses.TryAdd(action.Id, p))
                {
                    throw new Exception("Could not persist process to memory");
                }

                if (cancelToken.HasValue)
                {
                    CancellationToken token = cancelToken.GetValueOrDefault();

                    while (true)
                    {
                        if (token.IsCancellationRequested)
                        {
                            p.CancelErrorRead();
                            p.CancelOutputRead();
                            p.Kill();
                            return Task.FromCanceled(token);
                        }
                        p.WaitForExit(5000);
                    }
                }
                else
                {
                    //wait idenfinitely for the process to finish
                    p.WaitForExit();
                }
            }

            return null;
        }

        public static bool TryCancel(List<ProcessAction> actions)
        {
            var responses = new List<bool>();
            foreach (var item in actions)
            {
                bool resp = TryCancel(item);
                responses.Add(resp);
            }

            return responses.All(b => b == true);
        }

        public static bool TryCancel(ProcessAction action)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }

            if (!_runningProcesses.TryGetValue(action.Id, out Process runningProcess))
            {
                return false;
            }

            try
            {
                runningProcess.Kill();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Cancel(ProcessAction action)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }

            if (!_runningProcesses.TryGetValue(action.Id, out Process runningProcess))
            {
                throw new Exception($"Failed to find process that exist with Id {action.Id}");
            }

            try
            {
                runningProcess.Kill();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to kill process with id {action.Id}", ex);
            }
        }
    }
}
