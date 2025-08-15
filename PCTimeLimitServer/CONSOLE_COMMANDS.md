# PCTimeLimit Server Console Commands

The PCTimeLimit Server now includes an interactive console command system that allows administrators to manage the server while it's running.

## Available Commands

### `help`
Displays a list of all available commands with descriptions.

### `status`
Shows current server status including:
- Number of connected clients
- Total registered accounts
- Path to accounts data file
- Server running status

### `list-users`
Lists all registered usernames in the system.

### `clear-user-data`
**⚠️ DANGEROUS OPERATION** - Clears all user accounts and data.
- Requires confirmation (type "yes" to proceed)
- Permanently deletes all user accounts
- Cannot be undone
- Removes both memory data and the accounts file

### `quit` or `exit`
Gracefully shuts down the server.

## Usage

1. Start the server normally
2. The console will display "Type 'help' for available commands"
3. Type commands directly into the console
4. Commands are processed in real-time while the server continues to handle client connections

## Security Notes

- The `clear-user-data` command requires explicit confirmation
- All commands are processed locally on the server console
- No remote command execution is supported
- Commands are case-insensitive

## Example Session

```
PCTimeLimit Server Starting...
Server started on port 8888
Waiting for connections...
Type 'help' for available commands
Press Ctrl+C to stop the server

help
=== Available Commands ===
help              - Show this help message
clear-user-data   - Clear all user accounts and data
status            - Show server status and statistics
list-users        - List all registered users
quit/exit         - Exit the server
=======================

status
=== Server Status ===
Connected clients: 0
Total accounts: 3
Accounts file: C:\Users\...\AppData\Roaming\PC Time Limit Server\accounts.json
Server running: True
===================

list-users
=== Registered Users ===
- admin
- user1
- user2
Total: 3 users
=======================
```
