using System;
using System.Security;
using System.Text;

namespace sttz.InstallUnity
{

/// <summary>
/// Helper class to request admin password from user.
/// </summary>
public class Passworder
{
    /// <summary>
    /// Character used to mask the password.
    /// </summary>
    const string MASK_CHARACTER = "*";

    string pwd;

    /// <summary>
    /// Get the password. If the user was already prompted, the password
    /// will be returned immediately. Otherwise the function will block
    /// until the user has entered their password.
    /// </summary>
    /// <param name="prompt">Prompt to display before the password.</param>
    /// <returns>The entered password</returns>
    public string GetPassword(string prompt)
    {
        if (pwd != null) return pwd;

        var builder = new StringBuilder();
        Console.Write(prompt);
        while (true) {
            var info = Console.ReadKey(true);
            if (info.Key == ConsoleKey.Enter) {
                Console.WriteLine();
                pwd = builder.ToString();
                return pwd;
            } else if (!char.IsControl(info.KeyChar)) {
                builder.Append(info.KeyChar);
                Console.Write(MASK_CHARACTER);
            } else if (info.Key == ConsoleKey.Backspace && pwd.Length > 0) {
                builder.Remove(pwd.Length - 1, 1);
                Console.Write("\b \b");
            }
        }
    }
}

}