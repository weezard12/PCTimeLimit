using WindowsFirewallHelper;
using System;
using System.Diagnostics;
using System.Linq;
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

                // Check if firewall is enabled on any profile
                bool isFirewallEnabled = FirewallManager.Instance.Profiles.Any(p => p.IsActive && p.Enable);

                if (!isFirewallEnabled)
                {
                    // Firewall is disabled, so port is not blocked
                    return false;
                }

                var protocolType = GetProtocolType(protocol);

                // Check if there's an existing rule allowing THIS APPLICATION on this port
                foreach (var rule in FirewallManager.Instance.Rules)
                {
                    if (rule.IsEnable && rule.Action == FirewallAction.Allow)
                    {
                        // Check if the rule applies to our application
                        bool isForOurApp = false;
                        bool appliesToPort = false;

                        // Check application name
                        try
                        {
                            var appName = rule.GetType().GetProperty("ApplicationName")?.GetValue(rule) as string;
                            isForOurApp = !string.IsNullOrEmpty(appName) &&
                                         appName.Equals(currentExePath, StringComparison.OrdinalIgnoreCase);
                        }
                        catch { }

                        // Check port and protocol
                        try
                        {
                            var ruleProtocol = rule.GetType().GetProperty("Protocol")?.GetValue(rule);
                            var localPorts = rule.GetType().GetProperty("LocalPorts")?.GetValue(rule) as ushort[];

                            if (ruleProtocol != null && localPorts != null)
                            {
                                appliesToPort = ruleProtocol.Equals(protocolType) &&
                                              localPorts.Contains((ushort)port);
                            }
                            else if (ruleProtocol != null && localPorts == null)
                            {
                                // No specific ports means all ports
                                appliesToPort = true;
                            }
                        }
                        catch { }

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

                // Check if firewall is enabled
                bool isFirewallEnabled = FirewallManager.Instance.Profiles.Any(p => p.IsActive && p.Enable);

                if (!isFirewallEnabled)
                {
                    return false; // Firewall disabled
                }

                // Check if there's a rule for this application
                foreach (var rule in FirewallManager.Instance.Rules)
                {
                    if (rule.IsEnable && rule.Action == FirewallAction.Allow)
                    {
                        try
                        {
                            var appName = rule.GetType().GetProperty("ApplicationName")?.GetValue(rule) as string;
                            if (!string.IsNullOrEmpty(appName) &&
                                appName.Equals(currentExePath, StringComparison.OrdinalIgnoreCase))
                            {
                                return false; // App is allowed
                            }
                        }
                        catch { }
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
        /// This method requires administrator privileges.
        /// </summary>
        /// <param name="port">The port number to allow.</param>
        /// <param name="ruleName">The name for the firewall rule.</param>
        /// <param name="protocol">The protocol (TCP or UDP). Default is TCP.</param>
        /// <param name="direction">The direction (IN or OUT). Default is OUT.</param>
        /// <returns>True if the rule was added successfully, false otherwise.</returns>
        public static Task<bool> AddFirewallRuleAsync(int port, string ruleName, string protocol = "TCP", string direction = "OUT")
        {
            try
            {
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExePath))
                {
                    return Task.FromResult(false);
                }

                var protocolType = GetProtocolType(protocol);
                var directionType = direction.ToUpperInvariant() == "IN"
                    ? FirewallDirection.Inbound
                    : FirewallDirection.Outbound;

                var rule = FirewallManager.Instance.CreatePortRule(
                    ruleName,
                    FirewallAction.Allow,
                    (ushort)port,
                    protocolType
                );

                rule.Direction = directionType;

                // Associate with the application using reflection
                try
                {
                    var appNameProperty = rule.GetType().GetProperty("ApplicationName");
                    if (appNameProperty != null && appNameProperty.CanWrite)
                    {
                        appNameProperty.SetValue(rule, currentExePath);
                    }
                }
                catch { }

                FirewallManager.Instance.Rules.Add(rule);
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Adds a firewall rule to allow the current application to communicate freely (all ports).
        /// This method requires administrator privileges.
        /// </summary>
        /// <param name="ruleName">The name for the firewall rule.</param>
        /// <returns>True if the rule was added successfully, false otherwise.</returns>
        public static Task<bool> AddApplicationFirewallRuleAsync(string ruleName)
        {
            try
            {
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExePath))
                {
                    return Task.FromResult(false);
                }

                var rule = FirewallManager.Instance.CreateApplicationRule(
                    ruleName,
                    FirewallAction.Allow,
                    currentExePath
                );

                rule.Direction = FirewallDirection.Outbound;

                FirewallManager.Instance.Rules.Add(rule);
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Removes a firewall rule by name.
        /// This method requires administrator privileges.
        /// </summary>
        /// <param name="ruleName">The name of the firewall rule to remove.</param>
        /// <returns>True if the rule was removed successfully, false otherwise.</returns>
        public static Task<bool> RemoveFirewallRuleAsync(string ruleName)
        {
            try
            {
                var rulesToRemove = FirewallManager.Instance.Rules
                    .Where(r => r.Name.Equals(ruleName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var rule in rulesToRemove)
                {
                    FirewallManager.Instance.Rules.Remove(rule);
                }

                return Task.FromResult(rulesToRemove.Count > 0);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Gets the FirewallProtocol enum value for TCP or UDP.
        /// </summary>
        private static FirewallProtocol GetProtocolType(string protocol)
        {
            return protocol.ToUpperInvariant() switch
            {
                "TCP" => FirewallProtocol.TCP,
                "UDP" => FirewallProtocol.UDP,
                _ => FirewallProtocol.TCP // Default to TCP
            };
        }
    }
}