# PCTimeLimit Admin

Parent control panel for managing PC time limits for children.

## Features

- **Account Management**: Create new accounts or login to existing ones
- **Server Connection**: Automatic connection to PCTimeLimit server
- **Modern UI**: Clean, professional interface
- **Input Validation**: Comprehensive validation for usernames and passwords

## Configuration

The application uses built-in server configuration located in `Configuration/ServerConfig.cs`:

```csharp
public static class ServerConfig
{
    public const string SERVER_ADDRESS = "localhost";
    public const int SERVER_PORT = 8888;
    
    // Validation settings
    public const int MIN_USERNAME_LENGTH = 3;
    public const int MIN_PASSWORD_LENGTH = 6;
    public const int MAX_USERNAME_LENGTH = 50;
    public const int MAX_PASSWORD_LENGTH = 100;
}
```

## Usage

1. **Start the PCTimeLimit Server** first:
   ```
   cd PCTimeLimitServer
   dotnet run
   ```

2. **Run the Admin Application**:
   ```
   cd PCTimeLimitAdmin
   dotnet run
   ```

3. **Login or Create Account**:
   - Enter username (3-50 characters)
   - Enter password (6-100 characters)
   - Click "Create Account" or "Login"

## Architecture

- **LoginWindow**: Handles user authentication
- **MainWindow**: Dashboard interface (work in progress)
- **TcpClientService**: Manages TCP communication with server
- **ServerConfig**: Centralized configuration settings

## Requirements

- .NET 9.0
- PCTimeLimit Server running on localhost:8888
- Windows (WPF application)
