using NetFwTypeLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PCTimeLimitAdmin
{
    /// <summary>
    /// Provides utilities for checking and managing Windows Firewall rules.
    /// </summary>
    public static class FirewallHelper
    {
        /// <summary>
        /// Checks if the current application has a firewall rule allowing it to communicate on the specified port.
        /// </summary>
        /// <param name="port">The port number to check.</param>
        /// <param name="protocol">The protocol (TCP or UDP). Default is TCP.</param>
        /// <returns>True if the firewall is blocking the port for this app, false otherwise.</returns>
        public static bool IsPortBlocked(int port, string protocol = "TCP")
        {
            try
            {
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExePath)) return true;

                Type tNetFwPolicy2 = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                if (tNetFwPolicy2 == null) return false;

                INetFwPolicy2 fwPolicy2 = (INetFwPolicy2)Activator.CreateInstance(tNetFwPolicy2);

                // Check if firewall is enabled
                if (!fwPolicy2.FirewallEnabled[NET_FW_PROFILE_TYPE2_.NET_FW_PROFILE2_DOMAIN] &&
                    !fwPolicy2.FirewallEnabled[NET_FW_PROFILE_TYPE2_.NET_FW_PROFILE2_PRIVATE] &&
                    !fwPolicy2.FirewallEnabled[NET_FW_PROFILE_TYPE2_.NET_FW_PROFILE2_PUBLIC])
                {
                    // Firewall is disabled, so port is not blocked
                    return false;
                }

                // Check if there's an existing rule allowing THIS APPLICATION on this port
                foreach (INetFwRule rule in fwPolicy2.Rules)
                {
                    if (rule.Enabled &&
                        rule.Action == NET_FW_ACTION_.NET_FW_ACTION_ALLOW &&
                        rule.Protocol == GetProtocolNumber(protocol))
                    {
                        // Check if the rule applies to our application
                        bool isForOurApp = !string.IsNullOrEmpty(rule.ApplicationName) &&
                                          rule.ApplicationName.Equals(currentExePath, StringComparison.OrdinalIgnoreCase);

                        // Check if the rule applies to our port (or all ports)
                        bool appliesToPort = string.IsNullOrEmpty(rule.LocalPorts) || // Empty means all ports
                                           rule.LocalPorts.Split(',').Any(p => p.Trim() == port.ToString());

                        if (isForOurApp && appliesToPort)
                        {
                            return false; // Our app is allowed on this port
                        }
                    }
                }

                // No allowing rule found for our app on this port
                return true;
            }
            catch
            {
                // If we can't determine, assume it might be blocked
                return true;
            }
        }

        /// <summary>
        /// Checks if the current application has a firewall rule allowing it to communicate.
        /// </summary>
        /// <returns>True if the app has an allowing rule, false otherwise.</returns>
        public static bool IsCurrentAppBlocked()
        {
            try
            {
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExePath)) return true;

                Type tNetFwPolicy2 = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                if (tNetFwPolicy2 == null) return true;

                INetFwPolicy2 fwPolicy2 = (INetFwPolicy2)Activator.CreateInstance(tNetFwPolicy2);

                // Check if firewall is enabled
                if (!fwPolicy2.FirewallEnabled[NET_FW_PROFILE_TYPE2_.NET_FW_PROFILE2_DOMAIN] &&
                    !fwPolicy2.FirewallEnabled[NET_FW_PROFILE_TYPE2_.NET_FW_PROFILE2_PRIVATE] &&
                    !fwPolicy2.FirewallEnabled[NET_FW_PROFILE_TYPE2_.NET_FW_PROFILE2_PUBLIC])
                {
                    return false; // Firewall disabled
                }

                // Check if there's a rule for this application
                foreach (INetFwRule rule in fwPolicy2.Rules)
                {
                    if (rule.Enabled &&
                        rule.Action == NET_FW_ACTION_.NET_FW_ACTION_ALLOW &&
                        !string.IsNullOrEmpty(rule.ApplicationName) &&
                        rule.ApplicationName.Equals(currentExePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return false; // App is allowed
                    }
                }

                return true; // No allowing rule found
            }
            catch
            {
                return true; // Assume blocked if we can't determine
            }
        }

        /// <summary>
        /// Adds a firewall rule to allow the current application to communicate on the specified port.
        /// This method requires administrator privileges and will trigger a UAC prompt.
        /// </summary>
        /// <param name="port">The port number to allow.</param>
        /// <param name="ruleName">The name for the firewall rule.</param>
        /// <param name="protocol">The protocol (TCP or UDP). Default is TCP.</param>
        /// <param name="direction">The direction (IN or OUT). Default is OUT.</param>
        /// <returns>True if the rule was added successfully, false otherwise.</returns>
        public static async Task<bool> AddFirewallRuleAsync(int port, string ruleName, string protocol = "TCP", string direction = "OUT")
        {
            try
            {
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExePath))
                {
                    return false;
                }

                // Build the netsh command to add firewall rule
                var arguments = $"advfirewall firewall add rule " +
                              $"name=\"{ruleName}\" " +
                              $"dir={direction} " +
                              $"action=allow " +
                              $"protocol={protocol} " +
                              $"localport={port} " +
                              $"program=\"{currentExePath}\" " +
                              $"enable=yes";

                var processInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = arguments,
                    Verb = "runas", // This triggers the UAC prompt
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(processInfo);
                if (process == null) return false;

                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User cancelled the UAC prompt
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Adds a firewall rule to allow the current application to communicate freely (all ports).
        /// This method requires administrator privileges and will trigger a UAC prompt.
        /// </summary>
        /// <param name="ruleName">The name for the firewall rule.</param>
        /// <returns>True if the rule was added successfully, false otherwise.</returns>
        public static async Task<bool> AddApplicationFirewallRuleAsync(string ruleName)
        {
            try
            {
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExePath))
                {
                    return false;
                }

                // Build the netsh command to add firewall rule for the application
                var arguments = $"advfirewall firewall add rule " +
                              $"name=\"{ruleName}\" " +
                              $"dir=out " +
                              $"action=allow " +
                              $"program=\"{currentExePath}\" " +
                              $"enable=yes";

                var processInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = arguments,
                    Verb = "runas", // This triggers the UAC prompt
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(processInfo);
                if (process == null) return false;

                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User cancelled the UAC prompt
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Removes a firewall rule by name.
        /// This method requires administrator privileges and will trigger a UAC prompt.
        /// </summary>
        /// <param name="ruleName">The name of the firewall rule to remove.</param>
        /// <returns>True if the rule was removed successfully, false otherwise.</returns>
        public static async Task<bool> RemoveFirewallRuleAsync(string ruleName)
        {
            try
            {
                var arguments = $"advfirewall firewall delete rule name=\"{ruleName}\"";

                var processInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = arguments,
                    Verb = "runas", // This triggers the UAC prompt
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(processInfo);
                if (process == null) return false;

                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User cancelled the UAC prompt
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the protocol number for TCP or UDP.
        /// </summary>
        private static int GetProtocolNumber(string protocol)
        {
            return protocol.ToUpperInvariant() switch
            {
                "TCP" => 6,
                "UDP" => 17,
                _ => 6 // Default to TCP
            };
        }
    }
}