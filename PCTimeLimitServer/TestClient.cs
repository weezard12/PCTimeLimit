using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PCTimeLimitServer;

public class TestClient
{
    private readonly string _serverAddress;
    private readonly int _serverPort;
    
    public TestClient(string serverAddress = "localhost", int serverPort = 8888)
    {
        _serverAddress = serverAddress;
        _serverPort = serverPort;
    }
    
    public async Task<bool> TestCreateAccount(string username, string password)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_serverAddress, _serverPort);
            
            var request = new MessageRequest
            {
                Type = MessageType.CreateAccount,
                Data = new CreateAccountData
                {
                    Username = username,
                    Password = password
                }
            };
            
            var json = JsonSerializer.Serialize(request);
            var bytes = Encoding.UTF8.GetBytes(json);
            
            using var stream = client.GetStream();
            await stream.WriteAsync(bytes, 0, bytes.Length);
            
            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            var messageResponse = JsonSerializer.Deserialize<MessageResponse>(response);
            return messageResponse?.Success == true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating account: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> TestLogin(string username, string password)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_serverAddress, _serverPort);
            
            var request = new MessageRequest
            {
                Type = MessageType.Login,
                Data = new LoginData
                {
                    Username = username,
                    Password = password
                }
            };
            
            var json = JsonSerializer.Serialize(request);
            var bytes = Encoding.UTF8.GetBytes(json);
            
            using var stream = client.GetStream();
            await stream.WriteAsync(bytes, 0, bytes.Length);
            
            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            var messageResponse = JsonSerializer.Deserialize<MessageResponse>(response);
            return messageResponse?.Success == true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error logging in: {ex.Message}");
            return false;
        }
    }
    
    public static async Task RunTests()
    {
        Console.WriteLine("Starting TCP Server Tests...");
        Console.WriteLine("Note: This test requires the server to be running in another terminal.");
        Console.WriteLine("Run 'dotnet run' in another terminal first, then press Enter to continue...");
        Console.ReadLine();
        
        var testClient = new TestClient();
        
        // Test 1: Create account
        Console.WriteLine("\nTest 1: Creating account 'testuser'");
        var createResult = await testClient.TestCreateAccount("testuser", "password123");
        Console.WriteLine($"Create account result: {(createResult ? "SUCCESS" : "FAILED")}");
        
        // Test 2: Try to create duplicate account
        Console.WriteLine("\nTest 2: Creating duplicate account 'testuser'");
        var duplicateResult = await testClient.TestCreateAccount("testuser", "password123");
        Console.WriteLine($"Duplicate account result: {(duplicateResult ? "SUCCESS (should be FAILED)" : "FAILED (expected)")}");
        
        // Test 3: Login with correct credentials
        Console.WriteLine("\nTest 3: Login with correct credentials");
        var loginResult = await testClient.TestLogin("testuser", "password123");
        Console.WriteLine($"Login result: {(loginResult ? "SUCCESS" : "FAILED")}");
        
        // Test 4: Login with wrong password
        Console.WriteLine("\nTest 4: Login with wrong password");
        var wrongPasswordResult = await testClient.TestLogin("testuser", "wrongpassword");
        Console.WriteLine($"Wrong password result: {(wrongPasswordResult ? "SUCCESS (should be FAILED)" : "FAILED (expected)")}");
        
        // Test 5: Login with non-existent account
        Console.WriteLine("\nTest 5: Login with non-existent account");
        var nonExistentResult = await testClient.TestLogin("nonexistent", "password123");
        Console.WriteLine($"Non-existent account result: {(nonExistentResult ? "SUCCESS (should be FAILED)" : "FAILED (expected)")}");
        
        Console.WriteLine("\nTests completed!");
    }
}
