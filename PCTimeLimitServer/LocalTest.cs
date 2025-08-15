using System.Text.Json;

namespace PCTimeLimitServer;

public class LocalTest
{
    public static void RunAccountManagerTests()
    {
        Console.WriteLine("Running AccountManager Local Tests...");
        
        // Create a temporary account manager for testing
        var accountManager = new AccountManager();
        
        // Test 1: Create account
        Console.WriteLine("\nTest 1: Creating account 'testuser'");
        var createResult = accountManager.CreateAccount("testuser", "password123");
        Console.WriteLine($"Create account result: {(createResult.Success ? "SUCCESS" : "FAILED")}");
        if (!createResult.Success)
        {
            Console.WriteLine($"Error: {createResult.ErrorMessage}");
        }
        
        // Test 2: Try to create duplicate account
        Console.WriteLine("\nTest 2: Creating duplicate account 'testuser'");
        var duplicateResult = accountManager.CreateAccount("testuser", "password123");
        Console.WriteLine($"Duplicate account result: {(duplicateResult.Success ? "SUCCESS (should be FAILED)" : "FAILED (expected)")}");
        if (duplicateResult.Success)
        {
            Console.WriteLine("Error: Should not allow duplicate accounts");
        }
        else
        {
            Console.WriteLine($"Expected error: {duplicateResult.ErrorMessage}");
        }
        
        // Test 3: Login with correct credentials
        Console.WriteLine("\nTest 3: Login with correct credentials");
        var loginResult = accountManager.ValidateLogin("testuser", "password123");
        Console.WriteLine($"Login result: {(loginResult.Success ? "SUCCESS" : "FAILED")}");
        if (!loginResult.Success)
        {
            Console.WriteLine($"Error: {loginResult.ErrorMessage}");
        }
        
        // Test 4: Login with wrong password
        Console.WriteLine("\nTest 4: Login with wrong password");
        var wrongPasswordResult = accountManager.ValidateLogin("testuser", "wrongpassword");
        Console.WriteLine($"Wrong password result: {(wrongPasswordResult.Success ? "SUCCESS (should be FAILED)" : "FAILED (expected)")}");
        if (wrongPasswordResult.Success)
        {
            Console.WriteLine("Error: Should not allow wrong password");
        }
        else
        {
            Console.WriteLine($"Expected error: {wrongPasswordResult.ErrorMessage}");
        }
        
        // Test 5: Login with non-existent account
        Console.WriteLine("\nTest 5: Login with non-existent account");
        var nonExistentResult = accountManager.ValidateLogin("nonexistent", "password123");
        Console.WriteLine($"Non-existent account result: {(nonExistentResult.Success ? "SUCCESS (should be FAILED)" : "FAILED (expected)")}");
        if (nonExistentResult.Success)
        {
            Console.WriteLine("Error: Should not allow non-existent account");
        }
        else
        {
            Console.WriteLine($"Expected error: {nonExistentResult.ErrorMessage}");
        }
        
        // Test 6: Validation tests
        Console.WriteLine("\nTest 6: Input validation");
        var emptyUsernameResult = accountManager.CreateAccount("", "password123");
        Console.WriteLine($"Empty username result: {(emptyUsernameResult.Success ? "SUCCESS (should be FAILED)" : "FAILED (expected)")}");
        
        var shortUsernameResult = accountManager.CreateAccount("ab", "password123");
        Console.WriteLine($"Short username result: {(shortUsernameResult.Success ? "SUCCESS (should be FAILED)" : "FAILED (expected)")}");
        
        var shortPasswordResult = accountManager.CreateAccount("newuser", "123");
        Console.WriteLine($"Short password result: {(shortPasswordResult.Success ? "SUCCESS (should be FAILED)" : "FAILED (expected)")}");
        
        // Test 7: Account count
        Console.WriteLine($"\nTest 7: Account count: {accountManager.GetAccountCount()}");
        
        Console.WriteLine("\nLocal tests completed!");
    }
}
