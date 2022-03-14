using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace sttz.NiceConsoleLogger
{

public class ConsoleLogger : ILogger
{
    public static void ColorTest()
    {
        for (int i = -1; i < 16; i++) {
            ConsoleColor? bg = null, fg = null;
            if (i >= 0) {
                bg = (ConsoleColor)i;
                Console.WriteLine($"BackgroundColor = {bg}");
            }

            for (int j = -1; j < 16; j++) {
                if (j >= 0) {
                    fg = (ConsoleColor)j;
                }

                if (bg != null) Console.BackgroundColor = bg.Value;
                if (fg != null) Console.ForegroundColor = fg.Value;

                var name = "None";
                if (fg != null) name = fg.Value.ToString("G");

                Console.Write(" ");
                Console.Write(name);
                Console.Write(new string(' ', Console.BufferWidth - name.Length - 1));

                Console.ResetColor();
            }
        }
    }

    public static void Write(string input)
    {
        WriteColorString(ParseColorString(input));
    }

    public static void WriteLine(string input)
    {
        WriteColorLine(ParseColorString(input));
    }

    public Func<string, LogLevel, bool> Filter
    {
        get { return _filter; }
        set {
            if (value == null) {
                throw new ArgumentNullException(nameof(value));
            }
            _filter = value;
        }
    }
    Func<string, LogLevel, bool> _filter;

    public string Name { get; }

    internal IExternalScopeProvider ScopeProvider { get; set; }

    public ConsoleLogger(string name, Func<string, LogLevel, bool> filter, bool includeScopes)
            : this(name, filter, includeScopes ? new LoggerExternalScopeProvider() : null)
    {
    }

    internal ConsoleLogger(string name, Func<string, LogLevel, bool> filter, IExternalScopeProvider scopeProvider)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));

        Name = name;
        Filter = filter ?? ((category, logLevel) => true);
        ScopeProvider = scopeProvider;
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        return ScopeProvider?.Push(state) ?? default;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel == LogLevel.None) {
            return false;
        }

        return Filter(Name, logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        if (formatter == null) throw new ArgumentNullException(nameof(formatter));

        var message = formatter(state, exception);
        if (!string.IsNullOrEmpty(message) || exception != null) {
            var builder = recycledBuilder;
            if (builder == null) {
                builder = new StringBuilder();
            }

            var levelColor = GetLogLevelColor(logLevel);
            if (levelColor != null) {
                builder.Append("<");
                builder.Append(levelColor);
                builder.Append(">");
            }

            var levelPrefix = GetLogLevelPrefix(logLevel);
            if (levelPrefix != null) {
                builder.Append(levelPrefix);
            }

            if (!string.IsNullOrEmpty(message)) {
                builder.Append(message);
            } else if (exception != null) {
                builder.Append(exception.ToString());
            }

            if (levelColor != null) {
                builder.Append("</");
                builder.Append(levelColor);
                builder.Append(">");
            }

            if (builder.Length > 0) {
                var input = builder.ToString();
                var colored = ParseColorString(input);
                WriteColorLine(colored);
            }

            builder.Clear();
            if (builder.Capacity > 1024) {
                builder.Capacity = 1024;
            }
            recycledBuilder = builder;
        }
    }

    static StringBuilder recycledBuilder;

    static string GetLogLevelPrefix(LogLevel logLevel)
    {
        switch (logLevel) {
            case LogLevel.Trace:
                return "TRACE: ";
            case LogLevel.Debug:
                return "DEBUG: ";
            case LogLevel.Information:
                return null;
            case LogLevel.Warning:
                return "WARN:  ";
            case LogLevel.Error:
                return "ERROR: ";
            case LogLevel.Critical:
                return "ERROR: ";
            default:
                throw new ArgumentOutOfRangeException(nameof(logLevel));
        }
    }

    static string GetLogLevelColor(LogLevel logLevel)
    {
        switch (logLevel) {
            case LogLevel.Trace:
                return "gray";
            case LogLevel.Debug:
                return "gray";
            case LogLevel.Information:
                return null;
            case LogLevel.Warning:
                return "yellow";
            case LogLevel.Error:
                return "red";
            case LogLevel.Critical:
                return "red";
            default:
                throw new ArgumentOutOfRangeException(nameof(logLevel));
        }
    }

    static void WriteColorString(IEnumerable<ColorString> input)
    {
        foreach (var fragment in input) {
            if (fragment.fgColor != null) Console.ForegroundColor = fragment.fgColor.Value;
            if (fragment.bgColor != null) Console.BackgroundColor = fragment.bgColor.Value;
            Console.Write(fragment.text);
            Console.ResetColor();
        }
    }

    static void WriteColorLine(IEnumerable<ColorString> input)
    {
        WriteColorString(input);
        Console.WriteLine();
    }

    struct ColorString
    {
        public string text;
        public ConsoleColor? fgColor;
        public ConsoleColor? bgColor;
    }

    static Regex ColorTagRegex = new Regex(@"<(\/?)(\w+)(?: bg=(\w+))?>");

    static IEnumerable<ColorString> ParseColorString(string input)
    {
        if (string.IsNullOrEmpty(input)) {
            return new ColorString[] { new ColorString() { text = "" } };
        }

        var matches = ColorTagRegex.Matches(input);
        if (matches.Count == 0) {
            return new ColorString[] { new ColorString() { text = input } };
        }

        // test: <red bg=white>hello <blue>world</blue></red>!
        var colors = new List<ColorString>();
        var currentColors = new ColorString();
        colors.Add(currentColors);

        var pos = 0;
        var result = new List<ColorString>(matches.Count);
        foreach (Match match in matches) {
            if (match.Index > pos) {
                result.Add(new ColorString() {
                    text = input.Substring(pos, match.Index - pos),
                    fgColor = currentColors.fgColor,
                    bgColor = currentColors.bgColor
                });
            }

            ConsoleColor? fgColor = null, bgColor = null;
            if (!TryParseColor(match.Groups[2].Value, out fgColor)
                    || (match.Groups[1].Length == 0 && !TryParseColor(match.Groups[3].Value, out bgColor))) {
                result.Add(new ColorString() {
                    text = input.Substring(match.Index, match.Length),
                    fgColor = currentColors.fgColor,
                    bgColor = currentColors.bgColor
                });
                pos = match.Index + match.Length;
                continue;
            }

            if (match.Groups[1].Length == 0) {
                currentColors = new ColorString() {
                    fgColor = fgColor ?? currentColors.fgColor,
                    bgColor = bgColor ?? currentColors.bgColor
                };
                colors.Add(currentColors);
            } else {
                if (colors.Count == 1) {
                    throw new ArgumentException($"End console color tag </{match.Groups[2].Value}> before any opening tags");
                }
                var current = colors[colors.Count - 1].fgColor;
                if (colors[colors.Count - 1].fgColor != fgColor) {
                    throw new ArgumentException($"Umatched console color tag: Expected {current}, got {fgColor}");
                }
                colors.RemoveAt(colors.Count - 1);
                currentColors = colors[colors.Count - 1];
            }

            pos = match.Index + match.Length;
        }

        if (pos < input.Length) {
            result.Add(new ColorString() {
                text = input.Substring(pos),
                fgColor = currentColors.fgColor,
                bgColor = currentColors.bgColor
            });
        }

        return result;
    }

    static bool TryParseColor(string input, out ConsoleColor? color)
    {
        switch (input.ToLower()) {
            case "black":
                color = ConsoleColor.Black;
                return true;
            case "darkblue":
                color = ConsoleColor.DarkBlue;
                return true;
            case "darkgreen":
                color = ConsoleColor.DarkGreen;
                return true;
            case "darkcyan":
                color = ConsoleColor.DarkCyan;
                return true;
            case "darkred":
                color = ConsoleColor.DarkRed;
                return true;
            case "darkmagenta":
                color = ConsoleColor.DarkMagenta;
                return true;
            case "darkyellow":
                color = ConsoleColor.DarkYellow;
                return true;
            case "gray":
                color = ConsoleColor.Gray;
                return true;
            case "darkgray":
                color = ConsoleColor.DarkGray;
                return true;
            case "blue":
                color = ConsoleColor.Blue;
                return true;
            case "green":
                color = ConsoleColor.Green;
                return true;
            case "cyan":
                color = ConsoleColor.Cyan;
                return true;
            case "red":
                color = ConsoleColor.Red;
                return true;
            case "magenta":
                color = ConsoleColor.Magenta;
                return true;
            case "yellow":
                color = ConsoleColor.Yellow;
                return true;
            case "white":
                color = ConsoleColor.White;
                return true;
            case "inherit":
                color = null;
                return true;
            case "":
                color = null;
                return true;
            default:
                color = null;
                return false;
        }
    }
}

}
