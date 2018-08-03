using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions.Internal;

namespace sttz.ConsoleLogger
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
        return ScopeProvider?.Push(state) ?? NullScope.Instance;
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

    static void WriteColorLine(IEnumerable<ColorString> input)
    {
        foreach (var fragment in input) {
            if (fragment.fgColor != null) Console.ForegroundColor = fragment.fgColor.Value;
            if (fragment.bgColor != null) Console.BackgroundColor = fragment.bgColor.Value;
            Console.Write(fragment.text);
        }
        Console.WriteLine();
        Console.ResetColor();
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

            if (match.Groups[1].Length == 0) {
                currentColors = new ColorString() {
                    fgColor = ParseColor(match.Groups[2].Value) ?? currentColors.fgColor,
                    bgColor = ParseColor(match.Groups[3].Value) ?? currentColors.bgColor
                };
                colors.Add(currentColors);
            } else {
                if (colors.Count == 1) {
                    throw new ArgumentException($"End console color tag </{match.Groups[2].Value}> before any opening tags");
                }
                var color = ParseColor(match.Groups[2].Value);
                var current = colors[colors.Count - 1].fgColor;
                if (colors[colors.Count - 1].fgColor != color) {
                    throw new ArgumentException($"Umatched console color tag: Expected {current}, got {color}");
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

    static ConsoleColor? ParseColor(string input)
    {
        switch (input.ToLower()) {
            case "black":
                return ConsoleColor.Black;
            case "darkblue":
                return ConsoleColor.DarkBlue;
            case "darkgreen":
                return ConsoleColor.DarkGreen;
            case "darkcyan":
                return ConsoleColor.DarkCyan;
            case "darkred":
                return ConsoleColor.DarkRed;
            case "darkmagenta":
                return ConsoleColor.DarkMagenta;
            case "darkyellow":
                return ConsoleColor.DarkYellow;
            case "gray":
                return ConsoleColor.Gray;
            case "darkgray":
                return ConsoleColor.DarkGray;
            case "blue":
                return ConsoleColor.Blue;
            case "green":
                return ConsoleColor.Green;
            case "cyan":
                return ConsoleColor.Cyan;
            case "red":
                return ConsoleColor.Red;
            case "magenta":
                return ConsoleColor.Magenta;
            case "yellow":
                return ConsoleColor.Yellow;
            case "white":
                return ConsoleColor.White;
            case "inherit":
                return null;
            case "":
                return null;
            default:
                throw new ArgumentException("Invalid console color name: " + input);
        }
    }
}

}