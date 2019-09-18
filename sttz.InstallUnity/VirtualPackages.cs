using System.Collections.Generic;
using System.Linq;
using System;

namespace sttz.InstallUnity
{

/// <summary>
/// Implementation of UnityHub's dynamically generated packages.
/// </summary>
public static class VirtualPackages
{
    /// <summary>
    /// Enable virtual packages.
    /// </summary>
    /// <remarks>
    /// Packages will be injected into existing <see cref="VersionMetadata"/> in
    /// their <see cref="VersionMetadata.GetPackages"/> method.
    /// </remarks>
    public static void Enable()
    {
        VersionMetadata.OnGenerateVirtualPackages -= GeneratePackages;
        VersionMetadata.OnGenerateVirtualPackages += GeneratePackages;
    }

    /// <summary>
    /// Disable virtual packages. Virtual packages already generated will not be removed.
    /// </summary>
    public static void Disable()
    {
        VersionMetadata.OnGenerateVirtualPackages -= GeneratePackages;
    }

    static IEnumerable<PackageMetadata> GeneratePackages(VersionMetadata version, CachePlatform platform)
    {
        return Generator(version, platform).ToList();
    }

    static string[] Localizations_2018_1 = new string[] { "ja", "ko" };
    static string[] Localizations_2018_2 = new string[] { "ja", "ko", "zh-cn" };
    static string[] Localizations_2019_1 = new string[] { "ja", "ko", "zh-hans", "zh-hant" };

    static Dictionary<string, string> LanguageNames = new Dictionary<string, string>() {
        { "ja", "日本語" },
        { "ko", "한국어" },
        { "zh-cn", "简体中文" },
        { "zh-hant", "繁體中文" },
        { "zh-hans", "简体中文" },
    };

    static IEnumerable<PackageMetadata> Generator(VersionMetadata version, CachePlatform platform)
    {
        var v = version.version;

        // Documentation
        if (v.major >= 2018 && version.GetRawPackage(platform, "Documentation").name == null) {
            yield return new PackageMetadata() {
                name = "Documentation",
                description = "Offline Documentation",
                url = $"https://storage.googleapis.com/docscloudstorage/{v.major}.{v.minor}/UnityDocumentation.zip",
                install = true,
                destination = "{UNITY_PATH}",
                size = 350 * 1024 * 1024, // Conservative estimate based on 2019.2
                installedsize = 650 * 1024 * 1024, // "
            };
        }

        // Language packs
        if (v.major >= 2018) {
            string[] localizations;
            if (v.major == 2018 && v.minor == 1) {
                localizations = Localizations_2018_1;
            } else if (v.major == 2018) {
                localizations = Localizations_2018_2;
            } else {
                localizations = Localizations_2019_1;
            }

            foreach (var loc in localizations) {
                yield return new PackageMetadata() {
                    name = LanguageNames[loc],
                    description = $"{LanguageNames[loc]} Language Pack",
                    url = $"https://new-translate.unity3d.jp/v1/live/54/{v.major}.{v.minor}/{loc}",
                    fileName = $"{loc}.po",
                    destination = "{UNITY_PATH}/Unity.app/Contents/Localization",
                    size = 2 * 1024 * 1024, // Conservative estimate based on 2019.2
                    installedsize = 2 * 1024 * 1024, // "
                };
            }
        }

        // Android dependencies
        if (v.major >= 2019 && version.GetRawPackage(platform, "Android").name != null) {
            // Android SDK & NDK & stuff
            yield return new PackageMetadata() {
                name = "Android SDK & NDK Tools",
                description = "Android SDK & NDK Tools 26.1.1",
                url = $"https://dl.google.com/android/repository/sdk-tools-darwin-4333796.zip",
                destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK",
                size = 148 * 1024 * 1024,
                installedsize = 174 * 1024 * 1024,
                sync = "Android",
                eulaurl1 = "https://dl.google.com/dl/android/repository/repository2-1.xml",
                eulalabel1 = "Android SDK and NDK License Terms from Google",
                eulamessage = "Please review and accept the license terms before downloading and installing Android\'s SDK and NDK.",
            };

            yield return new PackageMetadata() {
                name = "Android SDK Platform Tools",
                description = "Android SDK Platform Tools 28.0.1",
                url = $"https://dl.google.com/android/repository/platform-tools_r28.0.1-darwin.zip",
                destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK",
                size = 5 * 1024 * 1024,
                installedsize = 16 * 1024 * 1024,
                hidden = true,
                sync = "Android SDK & NDK Tools",
            };

            yield return new PackageMetadata() {
                name = "Android SDK Build Tools",
                description = "Android SDK Build Tools 28.0.3",
                url = $"https://dl.google.com/android/repository/build-tools_r28.0.3-macosx.zip",
                destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/build-tools",
                size = 53 * 1024 * 1024,
                installedsize = 120 * 1024 * 1024,
                hidden = true,
                sync = "Android SDK & NDK Tools",
                renameFrom = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/build-tools/android-9",
                renameTo = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/build-tools/28.0.3"
            };

            yield return new PackageMetadata() {
                name = "Android SDK Platforms",
                description = "Android SDK Platforms 28 r06",
                url = $"https://dl.google.com/android/repository/platform-28_r06.zip",
                destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms",
                size = 61 * 1024 * 1024,
                installedsize = 121 * 1024 * 1024,
                hidden = true,
                sync = "Android SDK & NDK Tools",
                renameFrom = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms/android-9",
                renameTo = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms/android-28"
            };

            if (v.major == 2019 && v.minor <= 2) {
                yield return new PackageMetadata() {
                    name = "Android NDK 16b",
                    description = "Android NDK r16b",
                    url = $"https://dl.google.com/android/repository/android-ndk-r16b-darwin-x86_64.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer",
                    size = 770 * 1024 * 1024,
                    installedsize = 2700L * 1024 * 1024,
                    hidden = true,
                    sync = "Android SDK & NDK Tools",
                    renameFrom = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/android-ndk-r16b",
                    renameTo = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/NDK"
                };
            } else {
                yield return new PackageMetadata() {
                    name = "Android NDK 19",
                    description = "Android NDK r19",
                    url = $"https://dl.google.com/android/repository/android-ndk-r19-darwin-x86_64.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer",
                    size = 770 * 1024 * 1024,
                    installedsize = 2700L * 1024 * 1024,
                    hidden = true,
                    sync = "Android SDK & NDK Tools",
                    renameFrom = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/android-ndk-r19",
                    renameTo = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/NDK"
                };
            }

            // Android JDK
            if (v.major > 2019 || v.minor >= 2) {
                yield return new PackageMetadata() {
                    name = "OpenJDK",
                    description = "Android Open JDK 8u172-b11",
                    url = $"http://download.unity3d.com/download_unity/open-jdk/open-jdk-mac-x64/jdk8u172-b11_4be8440cc514099cfe1b50cbc74128f6955cd90fd5afe15ea7be60f832de67b4.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/OpenJDK",
                    size = 73 * 1024 * 1024,
                    installedsize = 165 * 1024 * 1024,
                    sync = "Android",
                };
            }
        }
    }
}

}
