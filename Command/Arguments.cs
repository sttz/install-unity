using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sttz.InstallUnity
{

/// <summary>
/// Exception representing an input error when parsing arguments.
/// </summary>
public class ArgumentsException : Exception
{
    /// <summary>
    /// Position of the argument related to the error.
    /// </summary>
    public int ArgumentIndex { get; protected set; }

    public ArgumentsException(string message, int index = -1) : base(message)
    {
        ArgumentIndex = index;
    }
}

/// <summary>
/// Simple arguments parser, modeled to be permissive and following GNU option syntax.
/// </summary>
/// <remarks>
/// Exhaustive list of features:
/// - Options and positional arguments
/// - Short (-x), long options (--xxx) and windows-style options (/x, /xxx)
/// - Short options can be combined (-xyz is equivalent to -x -y -z)
/// - Options can have an argument (-x arg, --xxx arg, --xxx=arg, --xxx:arg, -zyx arg)
/// - Options can have multiple arguments (either comma-separated or space-separated)
/// - Options can optionally be repeatable (-vv, -p one -p two)
/// - Options can optionally be required, arguments can optionally be optional
/// - Actions with own options and positional arguments
///   (action must be first positional argument, global options are allowed before action)
/// - Free ordering of options and arguments
/// - Usage of -- to terminate option parsing (everything that follows is treated as positional arguments)
/// - Currently only bool, string, enums and IList&lt;string&gt; are supported
/// - Generate help
/// - Generate error with faulty argument highlighted
/// 
/// Not supported:
/// - Short option argument without a space (-xARG)
/// - Long options with single dash (-xxx)
/// 
/// NOTE: Space-separated lists can eat the positional argument, use of -- required
/// NOTE: Optional arguments can eat positinal arguments, use of -- or = required
/// </remarks>
public class Arguments<T>
{
    // -------- API --------

    /// <summary>
    /// Define a new action. All following Options will become child options of this action.
    /// </summary>
    /// <param name="name">Name of the action (null = global / no action)</param>
    /// <param name="callback">Callback executed when the action has been selected</param>
    public Arguments<T> Action(string name, Action<T, string> callback = null)
    {
        if (name == null) {
            name = "";
        }

        if (actions.ContainsKey(name)) {
            throw new ArgumentException($"Action '{name}' already defined.");
        }
        definedAction = new ActionDef() {
            name = name,
            callback = callback
        };
        actions[definedAction.name] = definedAction;

        definedOption = null;

        return this;
    }

    /// <summary>
    /// Make the current action the default action (used when no action is explicitly set).
    /// </summary>
    public Arguments<T> DefaultAction()
    {
        if (definedAction == null) {
            throw new InvalidOperationException($"No current action set to make default");
        }
        defaultAction = definedAction.name;
        return this;
    }

    /// <summary>
    /// Define a boolean option (flag).
    /// By default the option is optional and not repeatable.
    /// Boolean opotions do not take an argument.
    /// </summary>
    /// <param name="setter">Callback called when the option is set</param>
    /// <param name="names">Names of the option</param>
    public Arguments<T> Option(Action<T, bool> setter, params string[] names)
    {
        AddOption(typeof(bool), new OptionDef<bool>() {
            action = definedAction?.name,
            names = names,
            position = -1,
            setter = setter
        });
        return this;
    }

    /// <summary>
    /// Define an option with a string argument.
    /// By default the option is optional and not repeatable, the argument required.
    /// </summary>
    /// <param name="setter">Callback called when the option is set</param>
    /// <param name="names">Names of the option</param>
    public Arguments<T> Option(Action<T, string> setter, params string[] names)
    {
        AddOption(typeof(string), new OptionDef<string>() {
            action = definedAction?.name,
            names = names,
            position = -1,
            requiresArgument = true,
            setter = setter
        });
        return this;
    }

    /// <summary>
    /// Define an option with a Enum argument.
    /// By default the option is optional and not repeatable, the argument required.
    /// </summary>
    /// <param name="setter">Callback called when the option is set</param>
    /// <param name="names">Names of the option</param>
    public Arguments<T> Option<TEnum>(Action<T, TEnum> setter, params string[] names) where TEnum : struct
    {
        if (!typeof(TEnum).IsEnum) throw new ArgumentException($"Type {typeof(TEnum)} is not an Enum", nameof(TEnum));

        AddOption(typeof(string), new OptionDef<string>() {
            action = definedAction?.name,
            names = names,
            position = -1,
            requiresArgument = true,
            argumentName = string.Join("|", Enum.GetNames(typeof(TEnum)).Select(n => n.ToLower())),
            setter = (target, input) => {
                if (string.IsNullOrEmpty(input)) {
                    setter(target, default);
                    return;
                }

                TEnum value;
                if (!Enum.TryParse<TEnum>(input, true, out value)) {
                    var values = Enum.GetNames(typeof(TEnum)).Select(v => "'" + v.ToLower() + "'");
                    var name = names.FirstOrDefault(n => n.Length > 1);
                    if (name == null) name = names[0];
                    throw new ArgumentsException($"Invalid value for {name}: '{input}' (must be {string.Join(", ", values)})");
                }

                setter(target, value);
            }
        });
        return this;
    }

    /// <summary>
    /// Define a list option.
    /// By default the option is optional and not repeatable, the argument required.
    /// </summary>
    /// <param name="setter">Callback called when the option is set</param>
    /// <param name="names">Names of the option</param>
    public Arguments<T> Option(Action<T, IList<string>> setter, params string[] names)
    {
        AddOption(typeof(IList<string>), new OptionDef<IList<string>>() {
            action = definedAction?.name,
            names = names,
            position = -1,
            requiresArgument = true,
            setter = setter
        });
        return this;
    }

    /// <summary>
    /// Define a positional option (argument).
    /// By default the option is optional.
    /// </summary>
    /// <param name="setter">Callback called when the option is set</param>
    /// <param name="position">Position of the argument</param>
    public Arguments<T> Option(Action<T, string> setter, int position)
    {
        if (position > 0) {
            var option = FindOption(definedAction?.name, position - 1);
            if (option == null) throw new ArgumentException($"Invalid position {position}: No Option at position {position-1} defined");
            if (option.repeatable) throw new InvalidOperationException("Cannot add another positional argument after a repeatable positional argument");
        }

        AddOption(typeof(string), new OptionDef<string>() {
            action = definedAction?.name,
            names = null,
            position = position,
            setter = setter
        });
        return this;
    }

    /// <summary>
    /// Make the last defined option repeatable.
    /// The callback will be called multiple times for each instance the option appears in the arguments.
    /// </summary>
    public Arguments<T> Repeatable(bool repeatable = true)
    {
        if (definedOption == null) throw new InvalidOperationException("Repeatable: No current option to operate on");
        definedOption.repeatable = repeatable;
        return this;
    }

    /// <summary>
    /// Make the last defined option required.
    /// </summary>
    public Arguments<T> Required(bool required = true)
    {
        if (definedOption == null) throw new InvalidOperationException("Required: No current option to operate on");
        if (definedOption is OptionDef<bool>) throw new InvalidOperationException("Required: Boolean options cannot be required");
        definedOption.required = required;
        return this;
    }

    /// <summary>
    /// Make the last defined option's argument required (default) or optional (pass false).
    /// </summary>
    public Arguments<T> OptionalArgument(bool optional = true)
    {
        if (definedOption == null) throw new InvalidOperationException("OptionalArgument: No current option to operate on");
        if (definedOption.position >= 0) throw new InvalidOperationException("OptionalArgument: Positional option doesn't have argument");
        if (definedOption is OptionDef<bool>) throw new InvalidOperationException("OptionalArgument: Boolean options cannot require argument");
        definedOption.requiresArgument = !optional;
        return this;
    }

    /// <summary>
    /// Set an argument name to use in the help.
    /// This method can be used for options that take arguments and for positional arguments.
    /// The default name is &lt;arg&gt;, the &lt;&gt; should be included in the name.
    /// </summary>
    public Arguments<T> ArgumentName(string name)
    {
        if (definedOption == null) throw new InvalidOperationException("ArgumentName: No current option to operate on");
        if (!TakesArgument(definedOption)) throw new InvalidOperationException("ArgumentName: Option does not take argument(s)");
        definedOption.argumentName = name;
        return this;
    }

    /// <summary>
    /// Provide a description to be shown in the help.
    /// Can be used for actions, options and positional arguments.
    /// </summary>
    public Arguments<T> Description(string desc)
    {
        if (definedAction == null && definedOption == null)
            throw new InvalidOperationException("Description: No action or option to operate on");
        if (definedOption != null) {
            definedOption.description = desc;
        } else {
            definedAction.description = desc;
        }
        return this;
    }

    /// <summary>
    /// Parse the given arguments and return the selected action.
    /// </summary>
    /// <param name="target">The target object</param>
    /// <param name="args">Input arguments to parse.</param>
    public void Parse(T target, string[] args)
    {
        var hasActions = actions.Count > 0;
        var explicitAction = false;
        var argPos = -1;
        var processOptions = true;

        parsedAction = defaultAction;

        for (int i = 0; i < args.Length; i++) {
            var arg = args[i];

            // -- terminates parsing of options and forces the 
            // rest to be interpreted as positional arguments
            if (arg == "--") {
                processOptions = false;
                continue;
            }

            var isOption = false;
            // - can be used to represent stdin
            if (processOptions && arg != "-") {
                // Long unix-style options: --xxx
                if (arg.StartsWith("--")) {
                    var name = GetName(arg.Substring(2));
                    var opt = FindOption(parsedAction, name, false);
                    if (opt != null) {
                        i += CallOption(opt, name, true, target, args, i);
                        isOption = true;
                    } else {
                        throw new ArgumentsException($"Unknown option: {name}", i);
                    }

                // Short and long windows-style options: /x /xxx
                } else if (arg.StartsWith("/")) {
                    var name = GetName(arg.Substring(1));
                    var opt = FindOption(parsedAction, name, null);
                    if (opt != null) {
                        i += CallOption(opt, name, true, target, args, i);
                        isOption = true;
                    } else {
                        // Don't treat this as an error, as it could be a path
                    }

                // Short unix-style options: -x -xyz
                } else if (arg.StartsWith("-")) {
                    for (int j = 1; j < arg.Length; j++) {
                        var name = arg[j].ToString();
                        var opt = FindOption(parsedAction, name, true);
                        if (opt != null) {
                            i += CallOption(opt, name, j == arg.Length - 1, target, args, i);
                            isOption = true;
                        } else {
                            throw new ArgumentsException($"Unknown short option: {name}", i);
                        }
                    }
                }
            }

            // Parse as positional argument
            if (!isOption) {
                argPos++;

                // First positional argument is parsed as action
                if (hasActions && argPos == 0 && actions.ContainsKey(arg)) {
                    explicitAction = true;
                    parsedAction = arg;

                } else {
                    var pos = argPos - (explicitAction ? 1 : 0);
                    var opt = FindOption(parsedAction, pos);
                    if (opt != null) {
                        CallOption(opt, target, arg);
                    } else {
                        throw new ArgumentsException($"Unexpected argument at position #{pos}: {arg}", i);
                    }
                }
            }
        }

        // Check for missing required options
        foreach (var option in options) {
            if (string.IsNullOrEmpty(option.action) || string.Equals(option.action, parsedAction, ActionComp)) {
                if (option.required && !option.wasSet) {
                    if (option.position >= 0) {
                        throw new ArgumentsException($"Required argument #{option.position} not set.");
                    } else {
                        throw new ArgumentsException($"Required option not set: {GetFirstLongName(option)}");
                    }
                }
            }

            // Reset wasSet in case Parse is called again
            option.wasSet = false;
        }

        // Action callback
        ActionDef action;
        if (actions.TryGetValue(parsedAction ?? "", out action) && action.callback != null) {
            action.callback(target, action.name);
        }
    }

    /// <summary>
    /// Write an exception to the console, recursively handling aggregate and inner exceptions.
    /// </summary>
    /// <param name="e">Exception to print</param>
    /// <param name="stackTrace">Print stack trace (never for outer exceptions)</param>
    /// <param name="enableColors">Colorize output</param>
    public static void WriteException(Exception e, string[] args, bool stackTrace, bool enableColors)
    {
        var arg = e as ArgumentsException;
        if (arg != null) {
            WriteArgumentsWithError(args, arg);
            return;
        }

        var agg = e as AggregateException;
        if (agg != null) {
            WriteSingleException(e, false, enableColors);
            foreach (var inner in agg.InnerExceptions) {
                WriteException(inner, args, stackTrace, enableColors);
            }
            return;
        }

        if (e.InnerException != null) {
            WriteSingleException(e, false, enableColors);
            WriteException(e.InnerException, args, stackTrace, enableColors);
            return;
        }

        WriteSingleException(e, stackTrace, enableColors);
    }

    static void WriteSingleException(Exception e, bool stackTrace, bool enableColors)
    {
        if (enableColors) Console.ForegroundColor = ConsoleColor.Red;
        
        Console.WriteLine(e.Message);
        
        if (stackTrace) {
            if (enableColors) Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(e.StackTrace);
        }

        Console.ResetColor();
    }

    // -------- Output --------

    /// <summary>
    /// Generate help.
    /// </summary>
    public string Help(string command, string header, string footer, int width = 80)
    {
        var sb = new StringBuilder();

        if (header != null) { 
            sb.AppendLine(header);
            sb.AppendLine();
        }

        // -- Main Usage
        sb.Append("USAGE: ");
        sb.Append(command);
        sb.Append(" ");

        // Global options
        var prefix = new string(' ', 8 + command.Length);
        var pos = prefix.Length;
        pos = OptionUsage(sb, prefix, pos, width, (option) => string.IsNullOrEmpty(option.action));

        // Action
        if (actions.Count > 0) {
            pos = WrappedAppend(sb, prefix, pos, width, "<action> ");
        }

        // Gloabl positional arguments
        pos = ArgumentUsage(sb, prefix, pos, width, (option) => string.IsNullOrEmpty(option.action));

        sb.AppendLine();
        sb.AppendLine();

        // -- Global Options
        if (options.Count(o => string.IsNullOrEmpty(o.action)) > 0) {
            if (actions.Count > 0) sb.Append("GLOBAL ");
            sb.AppendLine("OPTIONS:");
            ListOptions(sb, width, (option) => string.IsNullOrEmpty(option.action));

            sb.AppendLine();
            sb.AppendLine();
        }

        // -- Actions
        if (actions.Count > 0) {
            sb.AppendLine($"ACTIONS:\n");

            if (defaultAction != null && actions.TryGetValue(defaultAction, out var defaultActionDef)) {
                ActionUsage(sb, defaultActionDef, command, width);
            }

            foreach (var actionDef in actions.Values) {
                if (actionDef.name == "" || actionDef.name == defaultAction) continue;
                ActionUsage(sb, actionDef, command, width);
            }
        }

        if (footer != null) { 
            sb.AppendLine();
            sb.AppendLine(footer);
        }

        return sb.ToString();
    }

    void ActionUsage(StringBuilder sb, ActionDef action, string command, int width)
    {
        sb.AppendLine($"---- {action.name.ToUpper()}{(defaultAction == action.name ? " (default)" : "")}:");

        string prefix;
        if (action.description != null) {
            prefix = "     ";
            sb.Append(prefix);
            WordWrappedAppend(sb, prefix, prefix.Length, width, action.description);
            sb.AppendLine();
        }

        sb.AppendLine();

        // Action Usage
        var pos = 0;
        pos = Append(sb, pos, "USAGE: ");
        pos = Append(sb, pos, command);
        pos = Append(sb, pos, " [options] ");
        pos = Append(sb, pos, action.name == defaultAction ? $"[{action.name}]" : action.name);
        pos = Append(sb, pos, " ");

        prefix = new string(' ', 8 + command.Length);
        pos = OptionUsage(sb, prefix, pos, width, (option) => string.Equals(option.action, action.name, ActionComp));
        pos = ArgumentUsage(sb, prefix, pos, width, (option) => string.Equals(option.action, action.name, ActionComp));
        sb.AppendLine();

        sb.AppendLine();

        // Action options
        if (options.Count(option => string.Equals(option.action, action.name, ActionComp)) > 0) {
            sb.AppendLine("OPTIONS:");
            ListOptions(sb, width, (option) => string.Equals(option.action, action.name, ActionComp));
            sb.AppendLine();
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Helper function that prints options for usage.
    /// </summary>
    int OptionUsage(StringBuilder sb, string prefix, int pos, int width, Func<OptionDef, bool> filter)
    {
        foreach (var option in options) {
            if (option.position >= 0 || !filter(option)) continue;
            
            var name = GetFirstLongName(option);
            name = (name.Length == 1 ? "-" : "--") + name;
            if (TakesArgument(option)) name += " " + ArgumentName(option);
            if (option.repeatable) name += "...";
            if (!option.required) name = "[" + name + "]";

            pos = WrappedAppend(sb, prefix, pos, width, name + " ");
        }
        return pos;
    }

    /// <summary>
    /// Helper function that prints positional arguments for usage.
    /// </summary>
    int ArgumentUsage(StringBuilder sb, string prefix, int pos, int width, Func<OptionDef, bool> filter)
    {
        foreach (var option in options) {
            if (option.position < 0 || !filter(option)) continue;

            var name = ArgumentName(option);
            if (option.repeatable) name += "...";
            if (!option.required) name = "[" + name + "]";

            pos = WrappedAppend(sb, prefix, pos, width, name + " ");
        }
        return pos;
    }

    /// <summary>
    /// Helper function that prints option list.
    /// </summary>
    void ListOptions(StringBuilder sb, int width, Func<OptionDef, bool> filter)
    {
        var descIndent = 18;
        var pos = 0;
        var wrapPrefix = new string(' ', descIndent);
        foreach (var option in options) {
            if (!filter(option)) continue;

            if (option.position < 0) {
                var longName = GetFirstLongName(option, shortFallback: false);

                if (option.names.Length > 0 && option.names[0].Length == 1) {
                    pos = Append(sb, pos, " -");
                    pos = Append(sb, pos, option.names[0]);
                    if (longName != null) {
                        pos = Append(sb, pos, ", ");
                    } else {
                        pos = Append(sb, pos, "  ");
                    }
                } else {
                    pos = Append(sb, pos, "     ");
                }

                if (longName != null && longName.Length > 1) {
                    pos = Append(sb, pos, "--");
                    pos = Append(sb, pos, longName);
                    pos = Append(sb, pos, " ");
                }

                if (TakesArgument(option)) {
                    pos = Append(sb, pos, ArgumentName(option));
                    pos = Append(sb, pos, " ");
                }
            } else {
                pos = Append(sb, pos, " ");
                pos = Append(sb, pos, ArgumentName(option));
            }

            pos = Append(sb, pos, " ");

            if (option.description != null) {
                pos = Append(sb, pos, new string(' ', Math.Max(descIndent - pos, 0)));
                pos = WordWrappedAppend(sb, wrapPrefix, pos, width, option.description);
                sb.AppendLine();
                pos = 0;
            }
        }
    }

    /// <summary>
    /// Append to a string builder while tracking position.
    /// </summary>
    int Append(StringBuilder sb, int pos, string append)
    {
        sb.Append(append);
        return pos + append.Length;
    }

    /// <summary>
    /// Try to fit string into width or wrap it on a new line.
    /// </summary>
    int WrappedAppend(StringBuilder sb, string prefix, int pos, int width, string append)
    {
        pos += append.Length;
        if (pos > width) {
            sb.AppendLine();
            sb.Append(prefix);
            pos = prefix.Length + append.Length;
        }

        sb.Append(append);

        return pos;
    }

    /// <summary>
    /// Like WrappedAppend, but does it for each word individually.
    /// </summary>
    int WordWrappedAppend(StringBuilder sb, string prefix, int pos, int width, string append)
    {
        var words = append.Split(' ');
        foreach (var word in words) {
            pos = WrappedAppend(sb, prefix, pos, width, word + " ");
        }
        return pos;
    }

    /// <summary>
    /// Write a parse error to the console together with the used arguments, indicating where
    /// the error happened (if applicable).
    /// </summary>
    /// <param name="args">Arguments that were parsed</param>
    /// <param name="ex">Exception thrown during parsing</param>
    public static void WriteArgumentsWithError(string[] args, ArgumentsException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(ex.Message);
        Console.ResetColor();

        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = ConsoleColor.Gray;

        var cmdName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        Console.Write("$ ");
        Console.Write(cmdName);
        Console.Write(" ");

        var offset = cmdName.Length + 3;
        var originalFgColor = Console.ForegroundColor;
        for (int i = 0; i < args.Length; i++) {
            if (i > 0) Console.Write(" ");

            if (ex.ArgumentIndex == i) {
                Console.ForegroundColor = ConsoleColor.Red;
            }

            Console.Write(EscapeArgument(args[i]));

            if (ex.ArgumentIndex == i) {
                Console.ForegroundColor = originalFgColor;
            }

            if (ex.ArgumentIndex > i) {
                offset += args[i].Length + 1;
            }
        }

        Console.ResetColor();
        Console.WriteLine();

        if (ex.ArgumentIndex > 0) {
            Console.Write(new string(' ', offset));
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(new string('^', args[ex.ArgumentIndex].Length));
            Console.ResetColor();
        }
    }

    static char[] NeedQuotesChars = new [] { ' ', '\t', '\n' };

    /// <summary>
    /// Escape a command line argument.
    /// </summary>
    /// <remarks>
    /// Based on work from Nate McMaster, Licensed under the Apache License, Version 2.0.
    /// In turn based on this MSDN blog post:
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

    // -------- Internals --------

    /// <summary>
    /// Base option definition.
    /// </summary>
    class OptionDef
    {
        /// <summary>
        /// The action the option belongs to.
        /// </summary>
        public string action;
        /// <summary>
        /// The names (aliases) of the option (without prefix).
        /// </summary>
        public string[] names;
        /// <summary>
        /// The position of positional options or -1.
        /// </summary>
        public int position;
        /// <summary>
        /// Wether the option is repeatable.
        /// </summary>
        public bool repeatable;
        /// <summary>
        /// Wether the option is required.
        /// </summary>
        public bool required;
        /// <summary>
        /// Wether the option's argument is required.
        /// </summary>
        public bool requiresArgument;

        /// <summary>
        /// Name of argument(s), used in help.
        /// </summary>
        public string argumentName;
        /// <summary>
        /// Description shown in help.
        /// </summary>
        public string description;

        /// <summary>
        /// Used to track missing required arguments.
        /// </summary>
        public bool wasSet;
    }

    /// <summary>
    /// Generic option definition subclass that contains the typed callback.
    /// </summary>
    class OptionDef<TArg> : OptionDef
    {
        public Action<T, TArg> setter;
    }

    /// <summary>
    /// Action definition.
    /// </summary>
    class ActionDef
    {
        /// <summary>
        /// Name of the action.
        /// </summary>
        public string name;
        /// <summary>
        /// Description shown in help.
        /// </summary>
        public string description;
        /// <summary>
        /// Called when action has been selected.
        /// </summary>
        public Action<T, string> callback;
    }

    static StringComparison ActionComp = StringComparison.OrdinalIgnoreCase;

    ActionDef definedAction;
    OptionDef definedOption;
    Dictionary<string, ActionDef> actions = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase);
    string defaultAction;
    List<OptionDef> options = new List<OptionDef>();

    string parsedAction;

    /// <summary>
    /// Add an option definition
    /// </summary>
    void AddOption(Type t, OptionDef option)
    {
        if (option.position >= 0) {
            if (FindOption(definedAction?.name, option.position) != null) {
                throw new Exception($"Argument #{option.position} already defined.");
            }
        } else {
            foreach (var name in option.names) {
                if (FindOption(definedAction?.name, name, null) != null) {
                    throw new Exception($"Argument named '{name}' already defined.");
                }
            }
        }

        options.Add(option);
        definedOption = option;
    }

    /// <summary>
    /// Check wether an argument starts with an option prefix.
    /// </summary>
    bool IsOption(string arg)
    {
        if (arg.StartsWith("/")) {
            return FindOption(parsedAction, GetName(arg.Substring(1)), null) != null;
        }
        return arg.StartsWith("-");
    }

    /// <summary>
    /// Return an option's first long name or the first short name if it has no long names.
    /// </summary>
    string GetFirstLongName(OptionDef option, bool shortFallback = true)
    {
        if (option.names == null || option.names.Length == 0) return null;

        foreach (var name in option.names) {
            if (name.Length > 1) return name;
        }

        return shortFallback ? option.names[0] : null;
    }

    /// <summary>
    /// Return wether an option takes an argument.
    /// </summary>
    bool TakesArgument(OptionDef option)
    {
        return (option is OptionDef<string> || option is OptionDef<IList<string>>);
    }

    /// <summary>
    /// Return name to use for argument.
    /// </summary>
    string ArgumentName(OptionDef option)
    {
        return option.argumentName ?? "<arg>";
    }

    /// <summary>
    /// Parse the name out of an option with a combined argument, e.g. opt=value or opt:value.
    /// </summary>
    string GetName(string arg)
    {
        var index = arg.IndexOf('=');
        if (index > 0) {
            return arg.Substring(0, index);
        }

        index = arg.IndexOf(':');
        if (index > 0) {
            return arg.Substring(0, index);
        }

        return arg;
    }

    /// <summary>
    /// Parse the argument out of an option with a combined argument, e.g. opt=value or opt:value.
    /// </summary>
    string GetValue(string arg)
    {
        var index = arg.IndexOf('=');
        if (index > 0) {
            return arg.Substring(index + 1);
        }

        index = arg.IndexOf(':');
        if (index > 0) {
            return arg.Substring(index + 1);
        }

        return null;
    }

    /// <summary>
    /// Search for an option by name.
    /// </summary>
    /// <param name="action">Limit seach to this action (global options are always returned)</param>
    /// <param name="name">Name to search for</param>
    /// <param name="shortOption">Wether to search for short or long options or for both</param>
    OptionDef FindOption(string action, string name, bool? shortOption)
    {
        foreach (var option in options) {
            if (option.names == null || (!string.IsNullOrEmpty(option.action) && !string.Equals(option.action, action, ActionComp))) continue;

            foreach (var candidate in option.names) {
                if (shortOption == (candidate.Length != 1)) continue;
                if (string.Equals(name, candidate, ActionComp)) {
                    return option;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Search for a positional argument.
    /// </summary>
    /// <param name="action">Limit seach to this action (global arguments are always returned)</param>
    /// <param name="position">The position to search for</param>
    OptionDef FindOption(string action, int position)
    {
        if (position < 0) throw new ArgumentException($"Argument cannot be < 0", nameof(position));

        foreach (var option in options) {
            if (option.position < 0) continue;
            if (!string.IsNullOrEmpty(option.action) && !string.Equals(option.action, action, ActionComp)) continue;

            if (option.position == position || (option.repeatable && position >= option.position)) {
                return option;
            }
        }
        return null;
    }

    /// <summary>
    /// Parse the argument and call the option callback.
    /// </summary>
    /// <param name="option">Option to process</param>
    /// <param name="name">Name the option was appeared with</param>
    /// <param name="canTakArguments">Wether the option is in a position where it can take arguments</param>
    /// <param name="target">The target object</param>
    /// <param name="args">The input arguments</param>
    /// <param name="pos">The position of the option in the input arguments</param>
    int CallOption(OptionDef option, string name, bool canTakArguments, T target, string[] args, int pos)
    {
        // Check repeatability and set wasSet for requried testing
        if (option.wasSet && !option.repeatable) {
            throw new ArgumentsException($"Duplicate option: {name}", pos);
        }
        option.wasSet = true;

        // Bool option (flag), never has an argument
        if (option is OptionDef<bool>) {
            (option as OptionDef<bool>).setter(target, true);
            return 0;
        
        // String option with single argument
        } else if (option is OptionDef<string>) {
            // Argument can be separate (--xxx ARG) or combined (--xxx=ARG, --xxx:ARG)
            var value = GetValue(args[pos]);
            if (!canTakArguments || (value == null && (pos + 1 >= args.Length || IsOption(args[pos + 1])))) {
                if (option.requiresArgument) {
                    throw new ArgumentsException($"Missing argument for option: {name}", pos);
                } else {
                    (option as OptionDef<string>).setter(target, "");
                    return 0;
                }
            }
            if (value != null) {
                (option as OptionDef<string>).setter(target, value);
                return 0;
            } else {
                (option as OptionDef<string>).setter(target, args[pos + 1]);
                return 1;
            }
        
        // String option with multiple arguments
        } else if (option is OptionDef<IList<string>>) {
            // Arguments can be separate (--xxx one two three), separate delimited by comma (--xxx one,two,three),
            // or combined and separated by comma (--xxx=one,two,three or -xxx:one,two,three)
            var value = GetValue(args[pos]);
            if (!canTakArguments || (value == null && (pos + 1 >= args.Length || IsOption(args[pos + 1])))) {
                if (option.requiresArgument) {
                    throw new ArgumentsException($"Missing arguments for option: {name}", pos);
                } else {
                    (option as OptionDef<IList<string>>).setter(target, new List<string>());
                    return 0;
                }
            }
            var list = new List<string>();
            var index = pos + 1;
            if (value != null) {
                list.AddRange(value.Split(','));
            } else if (args[index].Contains(',')) {
                list.AddRange(args[index].Split(','));
                index++;
            } else {
                while (index < args.Length && !IsOption(args[index])) {
                    list.Add(args[index]);
                    index++;
                }
            }
            (option as OptionDef<IList<string>>).setter(target, list);
            return index - pos - 1;
        
        } else {
            throw new Exception($"Unhandled OptionDef subtype: {option.GetType().Name}");
        }
    }

    /// <summary>
    /// Call the callback of a positional option.
    /// </summary>
    /// <param name="option">Option to process</param>
    /// <param name="target">The target object</param>
    /// <param name="value">The positional value</param>
    void CallOption(OptionDef option, T target, string value)
    {
        option.wasSet = true;

        if (option is OptionDef<string>) {
            (option as OptionDef<string>).setter(target, value);
        
        } else {
            throw new Exception($"Unhandled OptionDef subtype for positional argument: {option.GetType().FullName}");
        }
    }
}

}
