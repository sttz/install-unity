using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace sttz.InstallUnity
{

/// <summary>
/// Unity version in the form of e.g. 2018.2.1f3
/// </summary>
public struct UnityVersion : IComparable, IComparable<UnityVersion>, IEquatable<UnityVersion>
{
    /// <summary>
    /// Unity release types.
    /// </summary>
    public enum Type: ushort {
        Undefined = '\0',
        /// <summary>
        /// Regular Unity release.
        /// </summary>
        Final = 'f',
        /// <summary>
        /// Unity patch release.
        /// </summary>
        Patch = 'p',
        /// <summary>
        /// Unity beta release.
        /// </summary>
        Beta  = 'b',
        /// <summary>
        /// Unity alpha release.
        /// </summary>
        Alpha = 'a'
    }

    // -------- Fields --------

    /// <summary>
    /// The major version number.
    /// </summary>
    public int major;

    /// <summary>
    /// The minor version number.
    /// </summary>
    public int minor;

    /// <summary>
    /// The patch version number.
    /// </summary>
    public int patch;

    /// <summary>
    /// The build type.
    /// </summary>
    public Type type;

    /// <summary>
    /// The build number.
    /// </summary>
    public int build;

    /// <summary>
    /// Unique hash of the build.
    /// </summary>
    public string hash;

    // -------- Configuration --------

    /// <summary>
    /// Regex used to parse Unity version strings.
    /// Everything except the major version is optional.
    /// </summary>
    static readonly Regex VERSION_REGEX = new Regex(@"^(\d+)?(?:\.(\d+)(?:\.(\d+))?)?(?:(\w)(?:(\d+))?)?(?: \(([0-9a-f]{12})\))?$");

    /// <summary>
    /// Regex to match a Unity version hash.
    /// </summary>
    static readonly Regex HASH_REGEX = new Regex(@"^([0-9a-f]{12})$");

    /// <summary>
    /// Get the sorting strength for a release type.
    /// </summary>
    public static int GetSortingForType(Type type)
    {
        switch (type) {
            case Type.Final:
                return 4;
            case Type.Patch:
                return 3;
            case Type.Beta:
                return 2;
            case Type.Alpha:
                return 1;
            default:
                return 0;
        }
    }

    /// <summary>
    /// Types sorted from unstable to stable.
    /// </summary>
    public static readonly Type[] SortedTypes = new Type[] {
        Type.Alpha, Type.Beta, Type.Patch, Type.Final, Type.Undefined
    };

    /// <summary>
    /// Enumerate release types starting with the given type in
    /// increasing stableness.
    /// </summary>
    public static IEnumerable<Type> EnumerateMoreStableTypes(Type startingWithType)
    {
        var index = Array.IndexOf(SortedTypes, startingWithType);
        if (index < 0) {
            throw new ArgumentException("Invalid release type: " + startingWithType, nameof(startingWithType));
        }

        for (int i = index; i < SortedTypes.Length; i++) {
            yield return SortedTypes[i];
        }
    }

    // -------- API --------

    /// <summary>
    /// Create a new Unity version.
    /// </summary>
    public UnityVersion(int major = -1, int minor = -1, int patch = -1, Type type = Type.Undefined, int build = -1, string hash = null)
    {
        this.major = major;
        this.minor = minor;
        this.patch = patch;
        this.type = type;
        this.build = build;
        this.hash = hash;
    }

    /// <summary>
    /// Create a new Unity version from a string.
    /// </summary>
    /// <remarks>
    /// The string must contain at least the major version. minor, patch and build are optional
    /// but the latter can only be set if the previous are (.e.g. setting patch but not minor 
    /// is not possible). The build type can always be set.
    /// 
    /// e.g.
    /// 2018
    /// 2018.1b
    /// 2018.1.1f3
    /// </remarks>
    public UnityVersion(string version)
    {
        major = minor = patch = build = -1;
        type = Type.Undefined;
        hash = null;

        if (string.IsNullOrEmpty(version)) return;

        var match = VERSION_REGEX.Match(version);
        if (match.Success) {
            if (match.Groups[1].Success) {
                major = int.Parse(match.Groups[1].Value);
                if (match.Groups[2].Success) {
                    minor = int.Parse(match.Groups[2].Value);
                    if (match.Groups[3].Success) {
                        patch = int.Parse(match.Groups[3].Value);
                        if (match.Groups[5].Success) {
                            build = int.Parse(match.Groups[5].Value);
                        }
                    }
                }
            }

            if (match.Groups[4].Success) {
                type = (Type)match.Groups[4].Value[0];
                if (!Enum.IsDefined(typeof(Type), type)) {
                    type = Type.Undefined;
                }
            }

            if (match.Groups[6].Success) {
                hash = match.Groups[6].Value;
            }
        } else {
            match = HASH_REGEX.Match(version);
            if (match.Success) {
                hash = match.Groups[1].Value;
            }
        }
    }

    /// <summary>
    /// Check wether the version is valid.
    /// </summary>
    /// <remarks>
    /// The version needs to contain at least the major version or build type.
    /// Minor can only be specified if major is.
    /// Patch can only be specified if minor is.
    /// Build can only be specified if patch is.
    /// </remarks>
    [JsonIgnore]
    public bool IsValid {
        get {
            if (major <= 0 && type == Type.Undefined && hash == null) return false;
            if (minor >= 0 && major < 0) return false;
            if (patch >= 0 && minor < 0) return false;
            if (build >= 0 && patch < 0) return false;
            return true;
        }
    }

    /// <summary>
    /// Wether all components of the version are set.
    /// </summary>
    [JsonIgnore]
    public bool IsFullVersion {
        get {
            return major >= 0 && minor >= 0 && patch >= 0 && type != Type.Undefined && build >= 0;
        }
    }

    /// <summary>
    /// Check if this version matches another, ignoring any components that aren't set.
    /// </summary>
    /// /// <remarks>
    /// Version component matching is done exactly, with the only difference that components
    /// can be -1, in which case that component is ignored.
    /// The type is however compared relatively and the order of the versions does matter
    /// in this case (e.g. `a.FuzzyMatch(b)` is not equivalent to `b.FuzzyMatch(a)`).
    /// In case the type is compared, lower priority types of `this` version match
    /// higher priority types of the `other` version. i.e. type Beta also matches 
    /// Patch and Final types but Final type does not match any other.
    /// </remarks>
    public bool FuzzyMatches(UnityVersion other)
    {
        if (major >= 0 && other.major >= 0 && major != other.major) return false;
        if (minor >= 0 && other.minor >= 0 && minor != other.minor) return false;
        if (patch >= 0 && other.patch >= 0 && patch != other.patch) return false;
        if (type != Type.Undefined && other.type != Type.Undefined 
            && GetSortingForType(type) > GetSortingForType(other.type)) return false;
        if (build >= 0 && other.build >= 0 && build != other.build) return false;
        if (hash != null && other.hash != null && hash != other.hash) return false;
        return true;
    }

    /// <summary>
    /// Check if either the version hashes match or the full version matches.
    /// </summary>
    public bool MatchesVersionOrHash(UnityVersion other)
    {
        if (hash != null && other.hash != null) {
            return hash == other.hash;
        }

        return major == other.major
            && minor == other.minor
            && patch == other.patch
            && type  == other.type
            && build == other.build;
    }

    public string ToString(bool withHash)
    {
        if (!IsValid) return $"undefined";

        var version = "";
        if (major >= 0) version = major.ToString();
        if (minor >= 0) version += "." + minor;
        if (patch >= 0) version += "." + patch;
        if (type != Type.Undefined) version += (char)type;
        if (build >= 0) version += build;
        if (withHash && hash != null) version += " (" + hash + ")";

        return version;
    }

    override public string ToString()
    {
        return ToString(true);
    }

    // -------- IComparable --------

    public int CompareTo(object obj)
    {
        if (obj is UnityVersion) {
            return CompareTo((UnityVersion)obj);
        } else {
            throw new ArgumentException("Argument is not a UnityVersion instance.", "obj");
        }
    }

    public int CompareTo(UnityVersion other)
    {
        int result;

        if (major >= 0 && other.major >= 0) {
            result = major.CompareTo(other.major);
            if (result != 0) return result;
        }

        if (minor >= 0 && other.minor >= 0) {
            result = minor.CompareTo(other.minor);
            if (result != 0) return result;
        }

        if (patch >= 0 && other.patch >= 0) {
            result = patch.CompareTo(other.patch);
            if (result != 0) return result;
        }

        if (type != Type.Undefined && other.type != Type.Undefined) {
            result = GetSortingForType(type).CompareTo(GetSortingForType(other.type));
            if (result != 0) return result;
        }

        if (build >= 0 && other.build >= 0) {
            result = build.CompareTo(other.build);
            if (result != 0) return result;
        }

        if (hash != null && other.hash != null) {
            result = string.CompareOrdinal(hash, other.hash);
            if (result != 0) return result;
        }

        return 0;
    }

    // -------- IEquatable --------

    override public bool Equals(object obj)
    {
        if (obj is UnityVersion) {
            return Equals((UnityVersion)obj);
        } else {
            return false;
        }
    }

    override public int GetHashCode()
    {
        int code = 0;
        code |= (major & 0x00000FFF) << 20;
        code |= (minor & 0x0000000F) << 16;
        code |= (patch & 0x0000000F) << 12;
        code |= ((ushort)type & 0x0000000F) << 8;
        code |= (build & 0x000000FF);
        return code;
    }

    public bool Equals(UnityVersion other)
    {
        return major == other.major
            && minor == other.minor
            && patch == other.patch
            && type  == other.type
            && build == other.build
            && (hash == null || other.hash == null || hash  == other.hash);
    }

    // -------- Operators --------

    public static bool operator ==(UnityVersion lhs, UnityVersion rhs)
    {
        return lhs.Equals(rhs);
    }

    public static bool operator !=(UnityVersion lhs, UnityVersion rhs)
    {
        return !lhs.Equals(rhs);
    }

    public static bool operator <(UnityVersion lhs, UnityVersion rhs)
    {
        return lhs.CompareTo(rhs) < 0;
    }

    public static bool operator >(UnityVersion lhs, UnityVersion rhs)
    {
        return lhs.CompareTo(rhs) > 0;
    }

    public static bool operator <=(UnityVersion lhs, UnityVersion rhs)
    {
        return lhs.CompareTo(rhs) <= 0;
    }

    public static bool operator >=(UnityVersion lhs, UnityVersion rhs)
    {
        return lhs.CompareTo(rhs) >= 0;
    }
}

}
