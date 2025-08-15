using System.Text.Json;

namespace PCTimeLimitServer;

public class TestLoginFix
{
    public static void TestLoginValidation()
    {
        Console.WriteLine("Testing Login Validation Fix...");
        
        var accountManager = new AccountManager();
        
        // Test 1: Try to login with non-existent account
        Console.WriteLine("\nTest 1: Login with non-existent account");
        var result1 = accountManager.ValidateLogin("nonexistent", "password123");
        Console.WriteLine($"Result: {(result1.Success ? "SUCCESS (SHOULD BE FAILED)" : "FAILED (CORRECT)")}");
        if (!result1.Success)
        {
            Console.WriteLine($"Error message: {result1.ErrorMessage}");
        }
        
        // Test 2: Create an account
        Console.WriteLine("\nTest 2: Create account 'testuser'");
        var createResult = accountManager.CreateAccount("testuser", "password123");
        Console.WriteLine($"Create result: {(createResult.Success ? "SUCCESS" : "FAILED")}");
        
        // Test 3: Login with correct credentials
        Console.WriteLine("\nTest 3: Login with correct credentials");
        var result3 = accountManager.ValidateLogin("testuser", "password123");
        Console.WriteLine($"Result: {(result3.Success ? "SUCCESS (CORRECT)" : "FAILED (SHOULD BE SUCCESS)")}");
        
        // Test 4: Login with wrong password
        Console.WriteLine("\nTest 4: Login with wrong password");
        var result4 = accountManager.ValidateLogin("testuser", "wrongpassword");
        Console.WriteLine($"Result: {(result4.Success ? "SUCCESS (SHOULD BE FAILED)" : "FAILED (CORRECT)")}");
        if (!result4.Success)
        {
            Console.WriteLine($"Error message: {result4.ErrorMessage}");
        }
        
        Console.WriteLine("\nLogin validation test completed!");
    }
}
