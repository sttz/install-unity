using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace sttz.InstallUnity
{

/// <summary>
/// Configuration of the Unity installer.
/// </summary>
public class Configuration
{
    [Description("After how many seconds the cache is considered to be outdated.")]
    public int cacheLifetime = 60 * 60 * 16; // 16 hours

    [Description("Maximum age of Unity releases to load when refreshing the cache (days).")]
    public int latestMaxAge = 90; // 90 days

    [Description("Delay between requests when scraping.")]
    public int scrapeDelayMs = 50;

    [Description("The default list of packages to install (null = use Unity's default).")]
    public string[] defaultPackages = null;

    [Description("Name of the subdirectory created to store downloaded packages ({0} = Unity version).")]
    public string downloadSubdirectory = "Unity {0}";

    [Description("Maximum number of concurrent downloads.")]
    public int maxConcurrentDownloads = 4;

    [Description("Maximum number of concurrent packages being installed.")]
    public int maxConcurrentInstalls = 1;

    [Description("Try to resume partial downloads.")]
    public bool resumeDownloads = true;

    [Description("Time in seconds until HTTP requests time out.")]
    public int requestTimeout = 30;

    [Description("How often to retry downloads.")]
    public int retryCount = 4;

    [Description("Delay in seconds before download is retried.")]
    public int retryDelay = 5;

    [Description("Draw progress bars for hashing and downloading.")]
    public bool progressBar = true;

    [Description("The interval in milliseconds in which the progress bars are updated.")]
    public int progressRefreshInterval = 50; // 20 fps

    [Description("Update the download status text every n progress refresh intervals.")]
    public int statusRefreshEvery = 20; // 1 fps

    [Description("Enable colored console output.")]
    public bool enableColoredOutput = true;

    [Description("Mac installation paths, separated by ; (first non-existing will be used, variables: {major} {minor} {patch} {type} {build} {hash}).")]
    public string installPathMac = 
          "/Applications/Unity {major}.{minor};"
        + "/Applications/Unity {major}.{minor}.{patch}{type}{build};"
        + "/Applications/Unity {major}.{minor}.{patch}{type}{build} ({hash})";

    // -------- Serialization --------

    /// <summary>
    /// Save the configuration as JSON to the given path.
    /// </summary>
    public bool Save(string path)
    {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(path, json);
            return true;
        } catch (Exception e) {
            UnityInstaller.GlobalLogger.LogError("Could not save configuration file: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// Load a configuration as JSON from the given path.
    /// </summary>
    public static Configuration Load(string path)
    {
        try {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<Configuration>(json);
        } catch (Exception e) {
            UnityInstaller.GlobalLogger.LogError("Could not read configuration file: " + e.Message);
            return null;
        }
    }

    // -------- Reflection --------

    /// <summary>
    /// List all available options
    /// </summary>
    /// <param name="name">Name of the option</param>
    /// <param name="type">Type of the option</param>
    /// <param name="description">Description of the option</param>
    public static IEnumerable<(string name, Type type, string description)> ListOptions()
    {
        var fields = typeof(Configuration).GetFields(BindingFlags.Instance | BindingFlags.Public);
        var info = new (string name, Type type, string description)[fields.Length];
        var index = 0;
        foreach (var field in fields) {
            string description = null;
            var attr = (DescriptionAttribute)field.GetCustomAttribute(typeof(DescriptionAttribute));
            if (attr != null) {
                description = attr.Description;
            }

            info[index++] = (
                field.Name, 
                field.FieldType, 
                description
            );
        }
        return info;
    }

    /// <summary>
    /// Set a configuration value by name.
    /// </summary>
    public void Set(string name, string value)
    {
        var field = typeof(Configuration).GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (field == null) {
            throw new ArgumentException($"No configuration value named {name} found.", nameof(name));
        }

        object parsed = null;
        if (field.FieldType == typeof(string)) {
            parsed = value;
        } else if (field.FieldType == typeof(bool)) {
            parsed = bool.Parse(value);
        } else if (field.FieldType == typeof(int)) {
            parsed = int.Parse(value);
        } else if (field.FieldType == typeof(string[])) {
            parsed = value.Split(':');
        } else {
            throw new Exception($"Field value type {field.FieldType} not yet supported.");
        }

        field.SetValue(this, parsed);
    }

    /// <summary>
    /// Get a configuration value by name.
    /// </summary>
    public string Get(string name)
    {
        var field = typeof(Configuration).GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (field == null) {
            throw new ArgumentException($"No configuration value named {name} found.", nameof(name));
        }

        if (field.FieldType == typeof(string[])) {
            var array = (string[])field.GetValue(this);
            if (array != null) {
                return string.Join(":", array);
            } else {
                return "";
            }
        } else {
            return field.GetValue(this).ToString();
        }
    }
}

}
