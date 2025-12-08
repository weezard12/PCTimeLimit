using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;

namespace PCTimeLimitService
{
    public partial class Service1 : ServiceBase
    {
        private const string ServiceLogFileName = "PCTimeLimitService.log";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "PCTimeLimit";
        private const int InvalidSessionId = -1;

        private readonly string _appExecutablePath;
        private readonly string _appProcessName;
        private readonly object _sync = new object();
        private Process _watchedProcess;
        private EventHandler _exitHandler;
        private Timer _restartTimer;
        private volatile bool _restartScheduled;
        private readonly TimeSpan _restartDelay = TimeSpan.FromSeconds(2);
        private string _lastLogMessage;
        private DateTime _lastLogTime;
        private DateTime _lastStartAttemptUtc;
        private DateTime _lastAttachUtc;
        private int _consecutiveFastExits;

        public Service1()
        {
            InitializeComponent();
            _appExecutablePath = @"C:\Program Files\PC Time Limit\PCTimeLimit.exe";
            _appProcessName = Path.GetFileNameWithoutExtension(_appExecutablePath);
            _restartTimer = new Timer(_ => RestartNow(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            CanHandleSessionChangeEvent = true;
        }

        protected override void OnStart(string[] args)
        {
            EnsureStartupEntry();
            TryEnsureWatchedProcess("service start");
        }

        protected override void OnStop()
        {
            lock (_sync)
            {
                CleanupWatchedProcess();
                _restartTimer?.Dispose();
                _restartTimer = null;
            }
        }

        protected override void OnSessionChange(SessionChangeDescription changeDescription)
        {
            base.OnSessionChange(changeDescription);

            switch (changeDescription.Reason)
            {
                case SessionChangeReason.SessionLogon:
                case SessionChangeReason.SessionUnlock:
                case SessionChangeReason.ConsoleConnect:
                case SessionChangeReason.RemoteConnect:
                    TryEnsureWatchedProcess("session change");
                    break;
            }
        }

        private void TryEnsureWatchedProcess(string reason)
        {
            lock (_sync)
            {
                if (_restartTimer != null)
                {
                    _restartTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                }
                _restartScheduled = false;

                if (_watchedProcess != null)
                {
                    try
                    {
                        if (!_watchedProcess.HasExited)
                        {
                            return;
                        }
                    }
                    catch
                    {
                        // fall through to restart
                    }

                    CleanupWatchedProcess();
                }

                EnsureStartupEntry();

                var existing = FindExistingProcess();
                if (existing != null)
                {
                    AttachProcess(existing, "attached to existing process");
                    return;
                }

                var started = StartAppForActiveUser();
                if (started != null)
                {
                    AttachProcess(started, $"started because of {reason}");
                }
            }
        }

        private Process FindExistingProcess()
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(_appProcessName))
                {
                    try
                    {
                        var path = process.MainModule != null ? process.MainModule.FileName : null;
                        if (path != null && path.Equals(_appExecutablePath, StringComparison.OrdinalIgnoreCase))
                        {
                            return process;
                        }
                    }
                    catch
                    {
                        // ignore processes we can't inspect
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("Failed to enumerate processes: " + ex.Message);
            }

            return null;
        }

        private void AttachProcess(Process process, string message)
        {
            try
            {
                if (process.HasExited)
                {
                    QueueRestart("process already exited before attach");
                    return;
                }

                _lastAttachUtc = DateTime.UtcNow;
                process.EnableRaisingEvents = true;
                _exitHandler = OnWatchedProcessExited;
                process.Exited += _exitHandler;
                _watchedProcess = process;
                _consecutiveFastExits = 0;
                WriteLog($"{message}; monitoring PID {_watchedProcess.Id}");
            }
            catch (Exception ex)
            {
                WriteLog("Failed to attach to process: " + ex.Message);
                CleanupWatchedProcess();
            }
        }

        private void OnWatchedProcessExited(object sender, EventArgs e)
        {
            TimeSpan runtime = TimeSpan.Zero;
            int exitCode = 0;
            try
            {
                if (sender is Process p)
                {
                    runtime = DateTime.UtcNow - _lastAttachUtc;
                    if (p.HasExited)
                    {
                        exitCode = p.ExitCode;
                    }
                }
            }
            catch
            {
                // ignore
            }

            bool fastExit = runtime < TimeSpan.FromSeconds(10);
            if (fastExit)
            {
                _consecutiveFastExits = Math.Min(_consecutiveFastExits + 1, 12);
            }
            else
            {
                _consecutiveFastExits = 0;
            }

            var delaySeconds = fastExit ? Math.Min(5 * _consecutiveFastExits, 60) : 2;
            WriteLog($"Watched process exited (code {exitCode}) after {runtime.TotalSeconds:F1}s; restart in {delaySeconds}s.");

            lock (_sync)
            {
                CleanupWatchedProcess();
            }
            QueueRestart(TimeSpan.FromSeconds(delaySeconds), "watched process exited");
        }

        private void QueueRestart(string reason)
        {
            QueueRestart(_restartDelay, reason);
        }

        private void QueueRestart(TimeSpan delay, string reason)
        {
            if (_restartTimer == null)
            {
                return;
            }

            lock (_sync)
            {
                if (_restartScheduled)
                {
                    return;
                }

                _restartScheduled = true;
                try
                {
                    _restartTimer.Change(delay, Timeout.InfiniteTimeSpan);
                }
                catch
                {
                    _restartScheduled = false;
                    ThreadPool.QueueUserWorkItem(_ => TryEnsureWatchedProcess(reason));
                }
            }

            WriteLog($"Scheduling restart in {delay.TotalSeconds:F0}s: {reason}");
        }

        private void RestartNow()
        {
            lock (_sync)
            {
                _restartScheduled = false;
            }

            TryEnsureWatchedProcess("timer restart");
        }

        private Process StartAppForActiveUser()
        {
            int sessionId = GetActiveSessionId();
            if (sessionId == InvalidSessionId)
            {
                WriteLog("No active user session found; skipping start.");
                return null;
            }

            var nowUtc = DateTime.UtcNow;
            if ((nowUtc - _lastStartAttemptUtc) < _restartDelay)
            {
                WriteLog("Start request ignored due to rapid repeat.");
                return null;
            }
            _lastStartAttemptUtc = nowUtc;

            if (!File.Exists(_appExecutablePath))
            {
                WriteLog("Monitored app missing: " + _appExecutablePath);
                return null;
            }

            if (!WTSQueryUserToken((uint)sessionId, out var userToken) || userToken == IntPtr.Zero)
            {
                WriteLog($"Unable to query user token for session {sessionId}. Win32 error: {Marshal.GetLastWin32Error()}");
                return null;
            }

            IntPtr primaryToken = IntPtr.Zero;
            IntPtr environmentBlock = IntPtr.Zero;

            try
            {
                var securityAttributes = new SECURITY_ATTRIBUTES
                {
                    nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES))
                };

                const int desiredAccess = (int)(
                    TOKEN_ACCESS.TOKEN_ASSIGN_PRIMARY |
                    TOKEN_ACCESS.TOKEN_DUPLICATE |
                    TOKEN_ACCESS.TOKEN_QUERY |
                    TOKEN_ACCESS.TOKEN_ADJUST_DEFAULT |
                    TOKEN_ACCESS.TOKEN_ADJUST_SESSIONID);

                if (!DuplicateTokenEx(userToken, desiredAccess, ref securityAttributes, SECURITY_IMPERSONATION_LEVEL.SecurityIdentification, TOKEN_TYPE.TokenPrimary, out primaryToken))
                {
                    WriteLog("DuplicateTokenEx failed. Win32 error: " + Marshal.GetLastWin32Error());
                    return null;
                }

                if (!CreateEnvironmentBlock(out environmentBlock, primaryToken, false))
                {
                    WriteLog("CreateEnvironmentBlock failed. Win32 error: " + Marshal.GetLastWin32Error());
                    return null;
                }

                var startupInfo = new STARTUPINFO
                {
                    cb = Marshal.SizeOf(typeof(STARTUPINFO)),
                    lpDesktop = @"winsta0\default"
                };

                var processInfo = new PROCESS_INFORMATION();
                var workingDirectory = Path.GetDirectoryName(_appExecutablePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                var commandLine = $"\"{_appExecutablePath}\"";
                var flags = CreateProcessFlags.CREATE_UNICODE_ENVIRONMENT | CreateProcessFlags.DETACHED_PROCESS;

                bool created = CreateProcessWithTokenW(
                    primaryToken,
                    LogonFlags.LOGON_WITH_PROFILE,
                    null,
                    commandLine,
                    (uint)flags,
                    environmentBlock,
                    workingDirectory,
                    ref startupInfo,
                    out processInfo);

                if (!created)
                {
                    created = CreateProcessAsUser(
                        primaryToken,
                        null,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        (uint)flags,
                        environmentBlock,
                        workingDirectory,
                        ref startupInfo,
                        out processInfo);
                }

                if (!created)
                {
                    WriteLog("Process creation failed. Win32 error: " + Marshal.GetLastWin32Error());
                    return null;
                }

                if (processInfo.hThread != IntPtr.Zero)
                {
                    CloseHandle(processInfo.hThread);
                }

                WriteLog($"Started PCTimeLimit for session {sessionId} (PID {processInfo.dwProcessId}).");

                Process startedProcess = null;
                try
                {
                    startedProcess = Process.GetProcessById(processInfo.dwProcessId);
                }
                catch (Exception ex)
                {
                    WriteLog("Unable to track started process: " + ex.Message);
                }
                finally
                {
                    if (processInfo.hProcess != IntPtr.Zero)
                    {
                        CloseHandle(processInfo.hProcess);
                    }
                }

                return startedProcess;
            }
            catch (Exception ex)
            {
                WriteLog("Failed to start app: " + ex);
                return null;
            }
            finally
            {
                if (environmentBlock != IntPtr.Zero)
                {
                    DestroyEnvironmentBlock(environmentBlock);
                }

                if (primaryToken != IntPtr.Zero)
                {
                    CloseHandle(primaryToken);
                }

                if (userToken != IntPtr.Zero)
                {
                    CloseHandle(userToken);
                }
            }
        }

        private void CleanupWatchedProcess()
        {
            if (_watchedProcess != null)
            {
                try
                {
                    if (_exitHandler != null)
                    {
                        _watchedProcess.Exited -= _exitHandler;
                    }
                    _watchedProcess.Dispose();
                }
                catch
                {
                    // ignore cleanup failures
                }
                finally
                {
                    _watchedProcess = null;
                    _exitHandler = null;
                }
            }
        }

        private void EnsureStartupEntry()
        {
            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(RunKey, true))
                {
                    if (key == null)
                    {
                        WriteLog("Failed to open HKLM run key.");
                        return;
                    }

                    var expected = $"\"{_appExecutablePath}\"";
                    var current = key.GetValue(RunValueName) as string;
                    if (!string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        key.SetValue(RunValueName, expected, RegistryValueKind.String);
                        WriteLog("Restored startup entry for PCTimeLimit.");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("Failed to verify startup entry: " + ex);
            }
        }

        private int GetActiveSessionId()
        {
            uint sessionId = WTSGetActiveConsoleSessionId();
            return sessionId == uint.MaxValue ? InvalidSessionId : (int)sessionId;
        }

        private void WriteLog(string message)
        {
            try
            {
                var now = DateTime.Now;
                if (string.Equals(message, _lastLogMessage, StringComparison.OrdinalIgnoreCase) &&
                    (now - _lastLogTime) < TimeSpan.FromSeconds(5))
                {
                    return;
                }

                _lastLogMessage = message;
                _lastLogTime = now;

                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ServiceLogFileName);
                var line = $"{now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";

                if (File.Exists(logPath))
                {
                    var info = new FileInfo(logPath);
                    if (info.Length > 64 * 1024)
                    {
                        File.Delete(logPath);
                    }
                }

                File.AppendAllText(logPath, line);
            }
            catch
            {
                // ignore logging failures
            }
        }

        #region Native interop

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bInheritHandle;
        }

        private enum SECURITY_IMPERSONATION_LEVEL
        {
            SecurityAnonymous = 0,
            SecurityIdentification = 1,
            SecurityImpersonation = 2,
            SecurityDelegation = 3
        }

        private enum TOKEN_TYPE
        {
            TokenPrimary = 1,
            TokenImpersonation
        }

        [Flags]
        private enum TOKEN_ACCESS
        {
            TOKEN_ASSIGN_PRIMARY = 0x0001,
            TOKEN_DUPLICATE = 0x0002,
            TOKEN_QUERY = 0x0008,
            TOKEN_ADJUST_DEFAULT = 0x0080,
            TOKEN_ADJUST_SESSIONID = 0x0100
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [Flags]
        private enum CreateProcessFlags : uint
        {
            DETACHED_PROCESS = 0x00000008,
            CREATE_NEW_PROCESS_GROUP = 0x00000200,
            CREATE_UNICODE_ENVIRONMENT = 0x00000400
        }

        [Flags]
        private enum LogonFlags : uint
        {
            LOGON_WITH_PROFILE = 0x00000001,
            LOGON_NETCREDENTIALS_ONLY = 0x00000002
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DuplicateTokenEx(
            IntPtr hExistingToken,
            int dwDesiredAccess,
            ref SECURITY_ATTRIBUTES lpTokenAttributes,
            SECURITY_IMPERSONATION_LEVEL ImpersonationLevel,
            TOKEN_TYPE TokenType,
            out IntPtr phNewToken);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessWithTokenW(
            IntPtr hToken,
            LogonFlags dwLogonFlags,
            string lpApplicationName,
            string lpCommandLine,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessAsUser(
            IntPtr hToken,
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSQueryUserToken(uint SessionId, out IntPtr phToken);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        #endregion
    }
}
