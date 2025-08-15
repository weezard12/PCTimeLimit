# PCTimeLimit Server

TCP server for managing PC time limits and user accounts.

## Features

- **Account Management**: Create and validate user accounts
- **TCP Communication**: Handle multiple client connections
- **JSON Storage**: Store accounts in AppData folder
- **No Encryption**: Passwords stored in plain text (server-side only)

## Storage Location

User accounts are stored in:
```
%APPDATA%\PC Time Limit Server\accounts.json
```

Example path: `C:\Users\Username\AppData\Roaming\PC Time Limit Server\accounts.json`

## Account Format

Accounts are stored in JSON format:

```json
[
  {
    "Username": "testuser",
    "Password": "password123",
    "CreatedAt": "2025-08-15T21:28:13.9677781Z",
    "LastLoginAt": "2025-08-15T21:28:14.0060039Z"
  }
]
```

## Usage

### Start the Server
```bash
cd PCTimeLimitServer
dotnet run
```

### Run Tests
```bash
# Local tests (no server required)
dotnet run localtest

# TCP tests (requires server running)
dotnet run test
```

## Configuration

- **Port**: 8888 (default)
- **Address**: 0.0.0.0 (all interfaces)
- **Storage**: AppData\Roaming\PC Time Limit Server

## Message Protocol

### Create Account
```json
{
  "Type": 1,
  "Data": {
    "Username": "username",
    "Password": "password"
  }
}
```

### Login
```json
{
  "Type": 2,
  "Data": {
    "Username": "username",
    "Password": "password"
  }
}
```

### Heartbeat
```json
{
  "Type": 3,
  "Data": null
}
```

## Requirements

- .NET 9.0
- Windows (for AppData folder access)
