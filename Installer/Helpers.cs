using System;
using System.IO;
using System.Text;

namespace sttz.InstallUnity
{

/// <summary>
/// Generic helper methods.
/// </summary>
public static class Helpers
{
    static readonly string[] SizeNames = new string[] {
        "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB"
    };

    /// <summary>
    /// Nicely format a size in bytes for printing.
    /// </summary>
    /// <param name="bytes">Size in bytes</param>
    /// <param name="format">Format string {0} is size and {1} size suffix</param>
    /// <returns>Size formatted with appropriate size suffix (B, KB, MB, etc)</returns>
    public static string FormatSize(long bytes, string format = "{0:0.00} {1}")
    {
        if (bytes <= 0) return "0 B";
        else if (bytes < 1024) return bytes + " B";

        var size = bytes / 1024.0;
        var index = Math.Min((int)Math.Log(size, 1024), SizeNames.Length - 1);
        var amount = size / Math.Pow(1024, index);
        return string.Format(format, amount, SizeNames[index]);
    }

    /// <summary>
    /// Convert byte data into a hexadecimal string.
    /// </summary>
    public static string ToHexString(byte[] data)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < data.Length; i++) {
            builder.Append(data[i].ToString("x2"));
        }
        return builder.ToString();
    }

    /// <summary>
    /// Generate a unique file or directory name.
    /// Appends (x) with increasing x until the name becomes unique.
    /// Returns the path unchanged if it doesn't exist.
    /// </summary>
    /// <param name="path">Input path to make unique</param>
    /// <returns>Path that doesn't exist</returns>
    public static string GenerateUniqueFileName(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var num = 2;

        string uniquePath;
        do {
            uniquePath = Path.Combine(dir, $"{name} ({num++}){ext}");
        } while (File.Exists(uniquePath) || Directory.Exists(uniquePath));

        return uniquePath;
    }

    /// <summary>
    /// Read a password from the console.
    /// The mask character will be used to provide feedback while the user is
    /// entering the password.
    /// </summary>
    public static string ReadPassword(char mask = '*')
    {
        var builder = new StringBuilder();
        while (true) {
            var info = Console.ReadKey(true);
            if (info.Key == ConsoleKey.Enter) {
                Console.WriteLine();
                return builder.ToString();
            } else if (!char.IsControl(info.KeyChar)) {
                builder.Append(info.KeyChar);
                Console.Write(mask);
            } else if (info.Key == ConsoleKey.Backspace && builder.Length > 0) {
                builder.Remove(builder.Length - 1, 1);
                Console.Write("\b \b");
            }
        }
    }
}

}