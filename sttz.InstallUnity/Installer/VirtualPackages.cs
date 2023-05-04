using System.Collections.Generic;
using System.Linq;
using System;

using static sttz.InstallUnity.UnityReleaseAPIClient;

namespace sttz.InstallUnity
{

/// <summary>
/// Implementation of UnityHub's dynamically generated packages.
/// </summary>
public static class VirtualPackages
{
    public static IEnumerable<Module> GeneratePackages(UnityVersion version, EditorDownload editor)
    {
        return Generator(version, editor).ToList();
    }

    static string[] Localizations_2018_1 = new string[] { "ja", "ko" };
    static string[] Localizations_2018_2 = new string[] { "ja", "ko", "zh-cn" };
    static string[] Localizations_2019_1 = new string[] { "ja", "ko", "zh-hans", "zh-hant" };

    static Dictionary<string, string> LanguageNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        { "ja", "日本語" },
        { "ko", "한국어" },
        { "zh-cn", "简体中文" },
        { "zh-hant", "繁體中文" },
        { "zh-hans", "简体中文" },
    };

    static IEnumerable<Module> Generator(UnityVersion version, EditorDownload editor)
    {
        var v = version;
        var allPackages = editor.AllModules;

        // Documentation
        if (v.major >= 2018 
                && !allPackages.ContainsKey("Documentation")
                && v.type != UnityVersion.Type.Alpha) {
            yield return new Module() {
                id = "Documentation",
                name = "Documentation",
                description = "Offline Documentation",
                url = $"https://storage.googleapis.com/docscloudstorage/{v.major}.{v.minor}/UnityDocumentation.zip",
                type = FileType.ZIP,
                preSelected = true,
                destination = "{UNITY_PATH}",
                downloadSize = FileSize.FromMegaBytes(350), // Conservative estimate based on 2019.2
                installedSize = FileSize.FromMegaBytes(650), // "
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
                yield return new Module() {
                    id = LanguageNames[loc],
                    name = LanguageNames[loc],
                    description = $"{LanguageNames[loc]} Language Pack",
                    url = $"https://new-translate.unity3d.jp/v1/live/54/{v.major}.{v.minor}/{loc}",
                    type = FileType.PO,
                    destination = "{UNITY_PATH}/Unity.app/Contents/Localization",
                    downloadSize = FileSize.FromMegaBytes(2), // Conservative estimate based on 2019.2
                    installedSize = FileSize.FromMegaBytes(2), // "
                };
            }
        }

        // Android dependencies
        if (v.major >= 2019 && allPackages.ContainsKey("Android")) {
            // Android SDK & NDK & stuff
            yield return new Module() {
                id = "Android SDK & NDK Tools",
                name = "Android SDK & NDK Tools",
                description = "Android SDK & NDK Tools 26.1.1",
                url = $"https://dl.google.com/android/repository/sdk-tools-darwin-4333796.zip",
                destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK",
                downloadSize = FileSize.FromMegaBytes(148),
                installedSize = FileSize.FromMegaBytes(174),
                parentModuleId = "Android",
                eula = new Eula[] {
                    new Eula() {
                        url = "https://dl.google.com/dl/android/repository/repository2-1.xml",
                        label = "Android SDK and NDK License Terms from Google",
                        message = "Please review and accept the license terms before downloading and installing Android\'s SDK and NDK.",
                    }
                },
            };

            // Android platform tools
            if (v.major < 2021) {
                yield return new Module() {
                    id = "Android SDK Platform Tools",
                    name = "Android SDK Platform Tools",
                    description = "Android SDK Platform Tools 28.0.1",
                    url = $"https://dl.google.com/android/repository/platform-tools_r28.0.1-darwin.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK",
                    downloadSize = FileSize.FromMegaBytes(5),
                    installedSize = FileSize.FromMegaBytes(16),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools",
                };
            } else if (v.major <= 2022) {
                yield return new Module() {
                    id = "Android SDK Platform Tools",
                    name = "Android SDK Platform Tools",
                    description = "Android SDK Platform Tools 30.0.4",
                    url = $"https://dl.google.com/android/repository/fbad467867e935dce68a0296b00e6d1e76f15b15.platform-tools_r30.0.4-darwin.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK",
                    downloadSize = FileSize.FromMegaBytes(10),
                    installedSize = FileSize.FromMegaBytes(30),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools",
                };
            } else {
                yield return new Module() {
                    id = "Android SDK Platform Tools",
                    name = "Android SDK Platform Tools",
                    description = "Android SDK Platform Tools 32.0.0",
                    url = $"https://dl.google.com/android/repository/platform-tools_r32.0.0-darwin.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK",
                    downloadSize = FileSize.FromBytes(18500000),
                    installedSize = FileSize.FromBytes(48684075),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools"
                };
            }

            // Android SDK platform & build tools
            if (v.major == 2019 && v.minor <= 3) {
                yield return new Module() {
                    id = "Android SDK Build Tools",
                    name = "Android SDK Build Tools",
                    description = "Android SDK Build Tools 28.0.3",
                    url = $"https://dl.google.com/android/repository/build-tools_r28.0.3-macosx.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/build-tools",
                    downloadSize = FileSize.FromMegaBytes(53),
                    installedSize = FileSize.FromMegaBytes(120),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools",
                    extractedPathRename = new PathRename() {
                        from = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/build-tools/android-9",
                        to = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/build-tools/28.0.3"
                    },
                };
                yield return new Module() {
                    id = "Android SDK Platforms",
                    name = "Android SDK Platforms",
                    description = "Android SDK Platforms 28 r06",
                    url = $"https://dl.google.com/android/repository/platform-28_r06.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms",
                    downloadSize = FileSize.FromMegaBytes(61),
                    installedSize = FileSize.FromMegaBytes(121),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools",
                    extractedPathRename = new PathRename() {
                        from = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms/android-9",
                        to = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms/android-28"
                    }
                };
            } else if (v.major <= 2022) {
                yield return new Module() {
                    id = "Android SDK Build Tools",
                    name = "Android SDK Build Tools",
                    description = "Android SDK Build Tools 30.0.2",
                    url = $"https://dl.google.com/android/repository/5a6ceea22103d8dec989aefcef309949c0c42f1d.build-tools_r30.0.2-macosx.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/build-tools",
                    downloadSize = FileSize.FromMegaBytes(49),
                    installedSize = FileSize.FromMegaBytes(129),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools",
                    extractedPathRename = new PathRename() {
                        from = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/build-tools/android-11",
                        to = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/build-tools/30.0.2"
                    }
                };
                yield return new Module() {
                    id = "Android SDK Platforms",
                    name = "Android SDK Platforms",
                    description = "Android SDK Platforms 30 r03",
                    url = $"https://dl.google.com/android/repository/platform-30_r03.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms",
                    downloadSize = FileSize.FromMegaBytes(52),
                    installedSize = FileSize.FromMegaBytes(116),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools",
                    extractedPathRename = new PathRename() {
                        from = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms/android-11",
                        to = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms/android-30"
                    }
                };
            } else {
                yield return new Module() {
                    id = "Android SDK Build Tools",
                    name = "Android SDK Build Tools",
                    description = "Android SDK Build Tools 32.0.0",
                    url = $"https://dl.google.com/android/repository/5219cc671e844de73762e969ace287c29d2e14cd.build-tools_r32-macosx.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/build-tools",
                    downloadSize = FileSize.FromBytes(50400000),
                    installedSize = FileSize.FromBytes(138655842),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools",
                    extractedPathRename = new PathRename() {
                        from = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/build-tools/android-12",
                        to = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/build-tools/32.0.0"
                    }
                };
                yield return new Module() {
                    id = "Android SDK Platforms",
                    name = "Android SDK Platforms",
                    description = "Android SDK Platforms 31",
                    url = $"https://dl.google.com/android/repository/platform-31_r01.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms",
                    downloadSize = FileSize.FromBytes(53900000),
                    installedSize = FileSize.FromBytes(91868884),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools",
                    extractedPathRename = new PathRename() {
                        from = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms/android-12",
                        to = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms/android-31"
                    }
                };
                yield return new Module() {
                    id = "Android SDK Platforms",
                    name = "Android SDK Platforms",
                    description = "Android SDK Platforms 32",
                    url = $"https://dl.google.com/android/repository/platform-32_r01.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms",
                    downloadSize = FileSize.FromBytes(63000000),
                    installedSize = FileSize.FromBytes(101630444),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools",
                    extractedPathRename = new PathRename() {
                        from = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms/android-12",
                        to = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/platforms/android-32"
                    }
                };
                yield return new Module() {
                    id = "Android SDK Command Line Tools",
                    name = "Android SDK Command Line Tools",
                    description = "Android SDK Command Line Tools 6.0",
                    url = $"https://dl.google.com/android/repository/commandlinetools-mac-8092744_latest.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/cmdline-tools",
                    downloadSize = FileSize.FromBytes(119650616),
                    installedSize = FileSize.FromBytes(119651596),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools",
                    extractedPathRename = new PathRename() {
                        from = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/cmdline-tools/cmdline-tools",
                        to = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/SDK/cmdline-tools/6.0"
                    }
                };
            }

            // Android NDK
            if (v.major == 2019 && v.minor <= 2) {
                yield return new Module() {
                    id = "Android NDK 16b",
                    name = "Android NDK 16b",
                    description = "Android NDK r16b",
                    url = $"https://dl.google.com/android/repository/android-ndk-r16b-darwin-x86_64.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer",
                    downloadSize = FileSize.FromMegaBytes(770),
                    installedSize = FileSize.FromMegaBytes(2700),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools",
                    extractedPathRename = new PathRename() {
                        from = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/android-ndk-r16b",
                        to = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/NDK"
                    }
                };
            } else if (v.major <= 2020) {
                yield return new Module() {
                    id = "Android NDK 19",
                    name = "Android NDK 19",
                    description = "Android NDK r19",
                    url = $"https://dl.google.com/android/repository/android-ndk-r19-darwin-x86_64.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer",
                    downloadSize = FileSize.FromMegaBytes(770),
                    installedSize = FileSize.FromMegaBytes(2700),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools",
                    extractedPathRename = new PathRename() {
                        from = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/android-ndk-r19",
                        to = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/NDK"
                    }
                };
            } else if (v.major <= 2022) {
                yield return new Module() {
                    id = "Android NDK 21d",
                    name = "Android NDK 21d",
                    description = "Android NDK r21d",
                    url = $"https://dl.google.com/android/repository/android-ndk-r21d-darwin-x86_64.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer",
                    downloadSize = FileSize.FromMegaBytes(1065),
                    installedSize = FileSize.FromMegaBytes(3922),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools",
                    extractedPathRename = new PathRename() {
                        from = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/android-ndk-r21d",
                        to = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/NDK"
                    }
                };
            } else {
                yield return new Module() {
                    id = "Android NDK 23b",
                    name = "Android NDK 23b",
                    description = "Android NDK r23b",
                    url = $"https://dl.google.com/android/repository/android-ndk-r23b-darwin.dmg",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/NDK",
                    downloadSize = FileSize.FromBytes(1400000000),
                    installedSize = FileSize.FromBytes(4254572698),
                    hidden = true,
                    parentModuleId = "Android SDK & NDK Tools",
                    extractedPathRename = new PathRename() {
                        from = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/NDK/Contents/NDK",
                        to = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/NDK"
                    }
                };
            }

            // Android JDK
            if (v.major >= 2023) {
                yield return new Module() {
                    id = "OpenJDK",
                    name = "OpenJDK",
                    description = "Android Open JDK 11.0.14.1+1",
                    url = $"https://download.unity3d.com/download_unity/open-jdk/open-jdk-mac-x64/jdk11.0.14.1-1_236fc2e31a8b6da32fbcf8624815f509c51605580cb2c6285e55510362f272f8.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/OpenJDK",
                    downloadSize = FileSize.FromBytes(118453231),
                    installedSize = FileSize.FromBytes(230230237),
                    parentModuleId = "Android",
                };
            } else if (v.major > 2019 || v.minor >= 2) {
                yield return new Module() {
                    id = "OpenJDK",
                    name = "OpenJDK",
                    description = "Android Open JDK 8u172-b11",
                    url = $"http://download.unity3d.com/download_unity/open-jdk/open-jdk-mac-x64/jdk8u172-b11_4be8440cc514099cfe1b50cbc74128f6955cd90fd5afe15ea7be60f832de67b4.zip",
                    destination = "{UNITY_PATH}/PlaybackEngines/AndroidPlayer/OpenJDK",
                    downloadSize = FileSize.FromMegaBytes(73),
                    installedSize = FileSize.FromMegaBytes(165),
                    parentModuleId = "Android",
                };
            }
        }
    }
}

}
