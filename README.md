# PC Time Limit - Multi-Computer Admin Control System

This system allows admin accounts to control time limits for multiple computers from a central server. Each admin can manage multiple child computers, setting daily time limits and monitoring their status.

## System Architecture

- **PCTimeLimitServer**: Central server managing admin accounts and computer registrations
- **PCTimeLimitAdmin**: Admin client for managing multiple computers
- **PCTimeLimit**: Child app that runs on controlled computers

## Features

- **Admin Account Management**: Create and manage admin accounts
- **Computer Registration**: Child computers register with admin accounts
- **Centralized Control**: Admins can set time limits for multiple computers
- **Real-time Status**: Monitor which computers are online/offline
- **Secure Authentication**: Admin credentials required for computer registration

## Setup Instructions

### 1. Start the Server

```bash
cd PCTimeLimitServer
dotnet run
```

The server will start on port 8888 and display available console commands.

### 2. Create Admin Accounts

Use the server console to create admin accounts:

```
create-admin <username> <password>
```

Or use the admin client to create accounts through the UI.

### 3. Run Child Apps on Computers

On each computer you want to control:

1. Run the PCTimeLimit app
2. Enter admin username and password when prompted
3. The computer will automatically register with the server
4. The app will start enforcing time limits

### 4. Manage Computers from Admin Client

1. Run PCTimeLimitAdmin
2. Login with admin credentials
3. Connect to the server
4. View all computers under your control
5. Set daily time limits for each computer

## Console Commands

- `help` - Show available commands
- `status` - Show server status and statistics
- `list-users` - List all registered users (admins marked with [ADMIN])
- `list-computers` - Show computer statistics
- `clear-user-data` - Clear all user accounts (use with caution)
- `quit` or `exit` - Stop the server

## Message Protocol

The system uses a TCP-based message protocol with the following message types:

- `CreateAccount` (1) - Create new user account
- `Login` (2) - Authenticate user
- `Heartbeat` (3) - Connection health check
- `RegisterComputer` (4) - Register computer with admin
- `UpdateComputerStatus` (5) - Update computer online/offline status
- `SetComputerTimeLimit` (6) - Set daily time limit for computer
- `GetComputersForAdmin` (7) - Get all computers for an admin

## File Structure

```
PCTimeLimit/
├── PCTimeLimitServer/          # Central server
├── PCTimeLimitAdmin/           # Admin management client
└── PCTimeLimit/                # Child app for controlled computers
```

## Data Storage

- **Accounts**: Stored in `%APPDATA%\PC Time Limit Server\accounts.json`
- **Computers**: Stored in `%APPDATA%\PC Time Limit Server\computers.json`
- **Child App Settings**: Stored in `%APPDATA%\PCTimeLimit\`

## Security Notes

- Passwords are stored in plain text (not recommended for production)
- No encryption of network communication
- Admin accounts have full control over their registered computers
- Computers can only be managed by their assigned admin

## Troubleshooting

### Server Connection Issues
- Ensure the server is running on port 8888
- Check firewall settings
- Verify network connectivity

### Computer Registration Issues
- Check admin credentials
- Ensure server is accessible from the child computer
- Verify computer ID generation

### Time Limit Issues
- Check if the child app is running
- Verify time limit settings in the admin client
- Check server logs for errors

## Development

To modify the system:

1. **Add new message types**: Update `MessageProtocol.cs` and server handlers
2. **Enhance security**: Implement password hashing and network encryption
3. **Add features**: Extend the admin interface for additional controls
4. **Improve monitoring**: Add logging and analytics capabilities

## License

This project is provided as-is for educational and development purposes.
