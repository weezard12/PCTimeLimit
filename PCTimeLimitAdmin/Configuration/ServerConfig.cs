namespace PCTimeLimitAdmin.Configuration;

public static class ServerConfig
{
    // Server connection settings
    public const string SERVER_ADDRESS = "localhost";
    public const int SERVER_PORT = 8888;
    
    // Connection timeout settings
    public const int CONNECTION_TIMEOUT_MS = 5000;
    public const int READ_TIMEOUT_MS = 3000;
    
    // Heartbeat settings
    public const int HEARTBEAT_INTERVAL_MS = 30000; // 30 seconds
    
    // Validation settings
    public const int MIN_USERNAME_LENGTH = 3;
    public const int MIN_PASSWORD_LENGTH = 6;
    public const int MAX_USERNAME_LENGTH = 50;
    public const int MAX_PASSWORD_LENGTH = 100;
}
