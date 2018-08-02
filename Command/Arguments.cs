using System;
using System.Collections;
using System.Collections.Generic;

namespace sttz.InstallUnity
{

public class ArgumentsException : Exception
{
    public ArgumentsException(string message) : base(message) { }
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
/// - Options can be optionally repeatable (-vv, -p one -p two)
/// - Option can optionally be required, arguments can optionally be optional
/// - Actions with own options and positional arguments
///   (action must be first positional argument, global options are allowed before action)
/// - Free ordering of options and arguments
/// - Usage of -- to terminate option parsing (everything that follows is treated as positional arguments)
/// - Currently only bool, string and IList&lt;string&gt; are supported
/// 
/// Not supported:
/// - Short option argument without a space (-xARG)
/// - Long options with single dash (-xxx)
/// 
/// NOTE: Space-separated lists can eat the positional argument, use of -- required
/// NOTE: Optional arguments can eat positinal arguments, use of -- or = required
/// </remarks>
public class Arguments
{
    class OptionDef
    {
        public string action;
        public string[] names;
        public int position;
        public bool repeatable;
        public bool required;
        public bool requiresArgument;

        public bool wasSet;
    }

    class OptionDef<T> : OptionDef
    {
        public Action<T> setter;
    }

    string currentAction;
    OptionDef currentOption;
    HashSet<string> actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    Dictionary<Type, List<OptionDef>> options = new Dictionary<Type, List<OptionDef>>();

    public string SelectedAction { get; protected set; }

    public Arguments Action(string name)
    {
        currentAction = name;
        currentOption = null;
        if (name != null) actions.Add(name);
        return this;
    }

    public Arguments Option(Action<bool> setter, params string[] names)
    {
        AddOption(typeof(bool), new OptionDef<bool>() {
            action = currentAction,
            names = names,
            position = -1,
            setter = setter
        });
        return this;
    }

    public Arguments Option(Action<string> setter, params string[] names)
    {
        AddOption(typeof(string), new OptionDef<string>() {
            action = currentAction,
            names = names,
            position = -1,
            requiresArgument = true,
            setter = setter
        });
        return this;
    }

    public Arguments Option(Action<IList<string>> setter, params string[] names)
    {
        AddOption(typeof(IList<string>), new OptionDef<IList<string>>() {
            action = currentAction,
            names = names,
            position = -1,
            requiresArgument = true,
            setter = setter
        });
        return this;
    }

    public Arguments Option(Action<string> setter, int position)
    {
        AddOption(typeof(string), new OptionDef<string>() {
            action = currentAction,
            names = null,
            position = position,
            setter = setter
        });
        return this;
    }

    public Arguments Repeatable(bool repeatable = true)
    {
        if (currentOption == null) throw new InvalidOperationException("Repeatable: No current option to operate on");
        if (currentOption.position >= 0) throw new InvalidOperationException("Repeatable: Positional option cannot be repeatable");
        currentOption.repeatable = repeatable;
        return this;
    }

    public Arguments Required(bool required = true)
    {
        if (currentOption == null) throw new InvalidOperationException("Required: No current option to operate on");
        if (currentOption is OptionDef<bool>) throw new InvalidOperationException("Required: Boolean options cannot be required");
        currentOption.required = required;
        return this;
    }

    public Arguments RequiresArgument(bool required = true)
    {
        if (currentOption == null) throw new InvalidOperationException("RequiresArgument: No current option to operate on");
        if (currentOption.position >= 0) throw new InvalidOperationException("RequiresArgument: Positional option doesn't have argument");
        if (currentOption is OptionDef<bool>) throw new InvalidOperationException("RequiresArgument: Boolean options cannot require argument");
        currentOption.requiresArgument = required;
        return this;
    }

    public string Parse(string[] args)
    {
        var hasActions = actions.Count > 0;
        var argPos = -1;
        var processOptions = true;

        for (int i = 0; i < args.Length; i++) {
            var arg = args[i];

            if (arg == "--") {
                processOptions = false;
                continue;
            }

            var isOption = false;
            if (processOptions && arg != "-") {
                if (arg.StartsWith("--")) {
                    var name = GetName(arg.Substring(2));
                    var opt = FindOption(SelectedAction, name, false);
                    if (opt != null) {
                        i += CallOption(opt, name, true, args, i);
                        isOption = true;
                    } else {
                        throw new ArgumentsException($"Unknown option: {name}");
                    }
                } else if (arg.StartsWith("/")) {
                    var name = GetName(arg.Substring(1));
                    var opt = FindOption(SelectedAction, name, null);
                    if (opt != null) {
                        i += CallOption(opt, name, true, args, i);
                        isOption = true;
                    } else {
                        throw new ArgumentsException($"Unknown option: {name}");
                    }
                } else if (arg.StartsWith('-')) {
                    for (int j = 1; j < arg.Length; j++) {
                        var name = arg[j].ToString();
                        var opt = FindOption(SelectedAction, name, true);
                        if (opt != null) {
                            i += CallOption(opt, name, j == arg.Length - 1, args, i);
                            isOption = true;
                        } else {
                            throw new ArgumentsException($"Unknown short option: {name}");
                        }
                    }
                }
            }

            if (!isOption) {
                argPos++;

                if (hasActions && argPos == 0 && actions.Contains(arg)) {
                    SelectedAction = arg;

                } else {
                    var pos = argPos - (SelectedAction != null ? 1 : 0);
                    var opt = FindOption(SelectedAction, pos);
                    if (opt != null) {
                        CallOption(opt, arg);
                    } else {
                        throw new ArgumentsException($"Unexpected argument at position #{pos}: {arg}");
                    }
                }
            }
        }

        foreach (var list in options.Values) {
            foreach (var option in list) {
                if (option.action != null && option.action != SelectedAction) continue;

                if (option.required && !option.wasSet) {
                    if (option.position >= 0) {
                        throw new ArgumentsException($"Required argument #{option.position} not set.");
                    } else {
                        throw new ArgumentsException($"Required option not set: {GetFirstLongName(option)}");
                    }
                }
            }
        }

        return SelectedAction;
    }

    void AddOption(Type t, OptionDef option)
    {
        List<OptionDef> list;
        if (!options.TryGetValue(t, out list)) {
            list = options[t] = new List<OptionDef>();
        }
        list.Add(option);
        currentOption = option;
    }

    bool IsOption(string arg)
    {
        return arg.StartsWith('-') || arg.StartsWith('/');
    }

    string GetFirstLongName(OptionDef option)
    {
        if (option.names == null || option.names.Length == 0) return null;

        foreach (var name in option.names) {
            if (name.Length > 0) return name;
        }

        return option.names[0];
    }

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

    OptionDef FindOption(string action, string name, bool? shortOption)
    {
        foreach (var pair in options) {
            foreach (var option in pair.Value) {
                if (option.names == null || (option.action != null && option.action != action)) continue;

                foreach (var candidate in option.names) {
                    if (shortOption == (candidate.Length != 1)) continue;
                    if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)) {
                        return option;
                    }
                }
            }
        }
        return null;
    }

    OptionDef FindOption(string action, int position)
    {
        if (position < 0) throw new ArgumentException($"Argument cannot be < 0", nameof(position));

        foreach (var pair in options) {
            foreach (var option in pair.Value) {
                if (option.action != null && option.action != action) continue;

                if (option.position == position) {
                    return option;
                }
            }
        }
        return null;
    }

    int CallOption(OptionDef option, string name, bool canTakArguments, string[] args, int pos)
    {
        if (option.wasSet && !option.repeatable) {
            throw new ArgumentsException($"Duplicate option: {name}");
        }
        option.wasSet = true;

        if (option is OptionDef<bool>) {
            (option as OptionDef<bool>).setter(true);
            return 0;
        
        } else if (option is OptionDef<string>) {
            var value = GetValue(args[pos]);
            if (!canTakArguments || (value == null && (pos + 1 >= args.Length || IsOption(args[pos + 1])))) {
                if (option.requiresArgument) {
                    throw new ArgumentsException($"Missing argument for option: {name}");
                } else {
                    (option as OptionDef<string>).setter("");
                    return 0;
                }
            }
            if (value != null) {
                (option as OptionDef<string>).setter(value);
                return 0;
            } else {
                (option as OptionDef<string>).setter(args[pos + 1]);
                return 1;
            }
        
        } else if (option is OptionDef<IList<string>>) {
            var value = GetValue(args[pos]);
            if (!canTakArguments || (value == null && (pos + 1 >= args.Length || IsOption(args[pos + 1])))) {
                if (option.requiresArgument) {
                    throw new ArgumentsException($"Missing arguments for option: {name}");
                } else {
                    (option as OptionDef<IList<string>>).setter(new List<string>());
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
            (option as OptionDef<IList<string>>).setter(list);
            return index - pos - 1;
        
        } else {
            throw new Exception($"Unhandled OptionDef subtype: {option.GetType().Name}");
        }
    }

    void CallOption(OptionDef option, string value)
    {
        option.wasSet = true;

        if (option is OptionDef<string>) {
            (option as OptionDef<string>).setter(value);
        
        } else {
            throw new Exception($"Unhandled OptionDef subtype for positional argument: {option.GetType().FullName}");
        }
    }
}

}