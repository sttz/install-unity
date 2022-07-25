using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        "MB", "GB", "TB", "PB", "EB", "ZB", "YB"
    };

    /// <summary>
    /// Nicely format a size in bytes for printing.
    /// </summary>
    /// <param name="bytes">Size in bytes</param>
    /// <param name="format">Format string {0} is size and {1} size suffix</param>
    /// <returns>Size formatted with appropriate size suffix (B, KB, MB, etc)</returns>
    public static string FormatSize(long bytes, string format = "{0:0.00} {1}")
    {
        if (bytes < 0) return "? KB";
        else if (bytes < 1024) return bytes + " KB";

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

    static char[] NeedQuotesChars = new [] { ' ', '\t', '\n' };

    /// <summary>
    /// Escape a command line argument.
    /// </summary>
    /// <remarks>
    /// Based on work from Nate McMaster, Licensed under the Apache License, Version 2.0.
    /// In turn based on MSDN blog post:
    /// https://blogs.msdn.microsoft.com/twistylittlepassagesallalike/2011/04/23/everyone-quotes-command-line-arguments-the-wrong-way/
    /// </remarks>
    public static string EscapeArgument(string arg)
    {
        var sb = new StringBuilder();

        var needsQuotes = arg.IndexOfAny(NeedQuotesChars) >= 0;
        var isQuoted = needsQuotes || (arg.Length > 1 && arg[0] == '"' && arg[arg.Length - 1] == '"');

        if (needsQuotes) {
            sb.Append('"');
        }

        for (int i = 0; i < arg.Length; ++i) {
            var backslashes = 0;

            // Consume all backslashes
            while (i < arg.Length && arg[i] == '\\') {
                backslashes++;
                i++;
            }

            if (i == arg.Length && isQuoted) {
                // Escape any backslashes at the end of the arg when the argument is also quoted.
                // This ensures the outside quote is interpreted as an argument delimiter
                sb.Append('\\', 2 * backslashes);
            } else if (i == arg.Length) {
                // At then end of the arg, which isn't quoted,
                // just add the backslashes, no need to escape
                sb.Append('\\', backslashes);
            } else if (arg[i] == '"') {
                // Escape any preceding backslashes and the quote
                sb.Append('\\', (2 * backslashes) + 1);
                sb.Append('"');
            } else {
                // Output any consumed backslashes and the character
                sb.Append('\\', backslashes);
                sb.Append(arg[i]);
            }
        }

        if (needsQuotes) {
            sb.Append('"');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Add a range of items to a collection.
    /// </summary>
    public static void AddRange<T>(this Collection<T> collection, IEnumerable<T> items)
    {
        foreach (var item in items) {
            collection.Add(item);
        }
    }

    /// <summary>
    /// Prompt the user on the console for an one-character answer.
    /// </summary>
    /// <param name="prompt">Prompt to ask user</param>
    /// <param name="options">Possible one-character answers (uppercase = default)</param>
    /// <returns>Chosen character out of given options</returns>
    public static char ConsolePrompt(string prompt, string options)
    {
        while (true) {
            Console.WriteLine();
            Console.Write($"{prompt} [{options}]: ");
            
            var input = Console.ReadKey();
            Console.WriteLine();
            
            // Choose default option on enter
            if (input.Key == ConsoleKey.Enter) {
                for (var i = 0; i < options.Length; i++) {
                    if (char.IsUpper(options[i])) {
                        return options[i];
                    }
                }
            }

            for (var i = 0; i < options.Length; i++) {
                if (char.ToLower(options[i]) == char.ToLower(input.KeyChar)) {
                    return options[i];
                }
            }

            // Repeat on invalid input
        }
    }

    /// <summary>
    /// Replace with custom StringComparison (only available in .netstandard).
    /// </summary>
    /// <param name="input">The string to replace in</param>
    /// <param name="oldValue">The old value to look for</param>
    /// <param name="newValue">The new value to replace it with</param>
    /// <param name="comparison">The comparison to use</param>
    /// <returns></returns>
    public static string Replace(string input, string oldValue, string newValue, StringComparison comparison)
    {
        if (input == null)
            throw new ArgumentException("input cannot be null", "input");
        if (string.IsNullOrEmpty(oldValue))
            throw new ArgumentException("oldValue cannot be null or empty", "oldValue");
        if (newValue == null)
            throw new ArgumentException("newValue cannot be null", "newValue");

        var output = "";
        var start = 0;
        while (start < input.Length) {
            var index = input.IndexOf(oldValue, start, comparison);
            if (index >= 0) {
                output += input.Substring(start, index - start) + newValue;
                start = index + oldValue.Length;
            } else {
                output += input.Substring(start);
                break;
            }
        }
        return output;
    }
}

}
