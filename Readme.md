# Install Unity Script

Unofficial Unity 3D installer for OS X.

# Requirements & Installation

If you're running macOS High Sierra (10.13), you can download and run the script without needing to install anything:<br>
`./install-unity.py --help`

On previous versions of macOS, Python is linked to an outdated version of OpenSSL and can no longer make connections to `unity3d.com`. Either download and install Python 2 from [the official Python website](https://www.python.org/downloads/) or install it using a package manager like [Homebrew](http://brew.sh) (`brew install python`).

# Introduction

Unity has begun to split their engine into different packages that can be installed separately and offer installers that allow to download and install only the desired packages.

The Install Unity Script taps into this infrastructure and allows to quickly download and install different versions of Unity, without having to download a different installer for each version.

e.g. installing multiple 5.x versions at once can be done using the following command:<br>
`./install-unity.py --package Unity 5.0 5.1 5.2 5.3`

This will install only the Unity editor, with no additional packages. The script will detect existing installations in folders starting with «Unity» in the Applications folder and will temporarily move them to install new versions or additional packages for existing versions.

Later, additional packages can be installed (platform packages only available with Unity 5.3+), e.g:<br>
`./install-unity.py --package Mac --package Windows --package Linux 5.3`

# Available Versions

Unity is packaged this way starting from Unity 5.0, the install script doesn’t support earlier versions.

The install script scans public Unity HTML pages and does not discover all versions available. For regular releases, this includes all versions of Unity 5.x.x. For patch releases, this includes only the newest 5 and for beta releases only the ones for the upcoming Unity version (if any).

For older patch and beta releases, specify the exact version number (e.g. 5.6.2p2) and the script will attempt to find it.

If that doesn't work, versions can be added manually by finding the URL to the Mac editor installer containing a 12-character hash code, e.g. `http://netstorage.unity3d.com/unity/2524e04062b4/MacEditorInstaller/Unity-5.3.0f4.pkg` and by calling:<br>
`./install-unity.py --discover URL`

# Selecting Versions

Versions can be specified with arbitrary precision, the install script will then select the latest available version that matches.

E.g. «5» will select the latest version of Unity 5, «5.3» the latest version of Unity 5.3 and «5.2.3» the latest version of Unity 5.2.3.

If no release type is specified, only regular (f) releases will be installed. Add a release letter to include more release types, p will include regular and patch releases, b will include regular, patch and beta releases. Amongst the releases of all selected types, the newest release will be installed.

E.g. «5.3p» will install the latest patch or the latest regular release for Unity 5.3. «5.4a» will install the latest Unity 5.4 release, be it alpha, beta, patch or regular.

# Selecting Packages

Some of the packages are only available in later versions of Unity. Prior to Unity 5.3, the main Unity editor installer includes all supported platforms and they cannot be installed separately.

Use the following command to show all available packages for a given version:<br>
`./install-unity.py --packages VERSION`

If no package is specified, the default packages (same as in the official Unity installer) will be installed. Otherwise, any number of `-p PACKAGE` or `--package PACKAGE` can be specified to select packages, selected packages that are not available for a given version will be ignored.

It's possible to save your own set of default packages, which will override Unity's set in case no packages have been specified. Add `--save` to the command to save the current `-p PACKAGE` and `--package PACKAGE` arguments as your custom default set. Call `--save` without any `-p` or `--package` arguments to clear your set of default packages or use `--unity-defaults` to use Unity's default set over your saved one.

# Offline Installation

The Unity install script can download and install the packages separately, allowing you to install Unity on multiple computers while only downloading the packages once.

By default, packages and all other required files are stored in `~/Library/Application Support/install-unity`. You can use `--data-dir` to change where those files are stored so you can more easily copy them.

First, download the packages using the `--download` flag. Execute the following command to download all required files to the `Unity_Data` folder on your Desktop:<br>
`./install-unity.py --download --all-packages --data-dir "~/Desktop/Unity_Data" VERSION`

Copy the script and Unity_Data folder to the target computer and then call:<br>
`./install-unity.py --install --all-packages --data-dir "~/Desktop/Unity_Data" VERSION`

Instead of installing all packages, you can select which packages to install using multiple `--package` flags. You can also specify multiple versions to download or install many Unity versions at once.

# Commands

All available commands:
```
usage: install-unity.py [-h] [--version] [--packages] [--download] [--install]
                        [--volume VOLUME] [-p PACKAGE] [--all-packages] [-k]
                        [--data-dir DATA_DIR] [-u]
                        [-l {release,patch,beta,alpha,all}]
                        [--discover DISCOVER] [--forget FORGET] [--save]
                        [--unity-defaults] [-v]
                        [VERSION [VERSION ...]]

Install Unity Script 0.1.3

positional arguments:
  VERSION               unity version to install packages from (only >= 5.0.0)

optional arguments:
  -h, --help            show this help message and exit
  --version             show program's version number and exit
  --packages            list available packages for the versions(s)
  --download            only download the version(s), don't install them
  --install             only install the version(s), they must have been
                        downloaded previously
  --volume VOLUME       set the target volume (must be a volume mountpoint)
  -p PACKAGE, --package PACKAGE
                        add package to download or install, absent = install
                        default packages
  --all-packages        install all packages instead of only the default ones
                        when no packages are selected
  -k, --keep            don't remove downloaded packages after installation
                        (implied when using --install)
  --data-dir DATA_DIR   directory to store packages, unity ini files, cache
                        and settings (default is in Application Support)
  -u, --update          force updating of cached version information
  -l {release,patch,beta,alpha,all}, --list {release,patch,beta,alpha,all}
                        list the cached unity versions
  --discover DISCOVER   manually discover a Unity packages url (link to unity-
                        VERSION-osx.ini or MacEditorInstaller url)
  --forget FORGET       remove a manually discovered version
  --save                save the current set of packages as defaults, used
                        when no packages are given (use with no packages to
                        reset)
  --unity-defaults      use the unity default packages instead of the custom
                        defaults that might have been saved
  -v, --verbose         show stacktrace when an error occurs
```

# Version History

#### 0.1.3 (2018-07-05)
* Fix discovery of Unity 2018.2 beta releases
* Fix warning caused by Unity Hub

#### 0.1.2 (2018-03-21)
* Support for discovering new Unity LTS releases
* Don't abort installation if target Unity directory is empty

#### 0.1.1 (2017-11-30)
* Fix OpenSSL version check being too picky (now allows unmatched versions like LibreSSL)
* Fix being asked for password more than once when installing multiple versions
* Fix error when installing multiple versions

#### 0.1.0 (2017-07-28)
* Files are now stored in `~/Library/Application Support/install-unity` by default
* Added `--data-dir` to override where files are stored (packages, installer ini files, cache and settings)
* Removed `--package-store` in favor of new `--data-dir`
* Unity installer ini files are stored together with the downloaded packages and cleaned up with them
* Guess the release notes URL of patch and beta releases to discover them automatically (only works if the full version number is given, e.g. 5.6.2p2)
* Store settings in a separate `settings.json` file (old settings in cache file will be migrated)
* Fixed an exception when a package is not found (@samizzo)

#### 0.0.9 (2017-07-06)
* Improved version selection (see [Selecting Versions](#selecting-versions))
* Fixed an exception occurring before installing
* Fixed an exception while cleaning up after install
* Fixed an exception in the --forget command

#### 0.0.8 (2017-07-04)
* Resume and retry downloads
* Show download size before entering password, also show install size
* Only show stacktrace with `-v` or `--verbose`

#### 0.0.7 (2017-06-27)
* Add option to save a custom set of default packages (see [Selecting Packages](#selecting-packages) for details)
* Fix `--download` refusing to download additional packages if the given Unity version is not installed

#### 0.0.6 (2017-05-31)
* Fix discovery of Unity 2017.1 versions
* Renamed `--list` to `--packages`, renamed `--list-versions` to just `--list` and added shortcut `-l`
* Only download list of patch and beta releases when necessary
* By default, `install-unity` will only list release versions, use `-l patch|beta|all` to load and list patch/beta releases. Installing and listing packages still works as before, just use e.g. `2017.1b` to install the latest beta version of Unity 2017.1
* A single release letter is now accepted as version string, e.g. just `b` for the latest beta
* Only ask for password after version and packages have been selected, giving a chance to review those choices

#### 0.0.5 (2017-05-01)
* Fix connecting to unity3d.com now that it requires TLS1.2 (python upgrade may be needed)
* Script now runs with Homebrew Python 2 when installed
* Improve display of Unity versions list
* Fix error reporting when catching exceptions (thanks to Sam Izzo)

#### 0.0.4 (2016-05-16)
* Detect if script is run as root and don't ask for password in that case
* Improve password handling so it won't be leaked in the process info

#### 0.0.3 (2016-03-02)
* Only install regular releases if no release type is specified (append «a» to a version to install the latest release of any type)
* Add fallback for release types, e.g. specifying a beta release will fall back to patch and regular releases if no beta release is available
* Scanning for available beta releases (@lacostej)
* Fix error when beta or alpha versions are installed (@lacostej)
* Print stack trace on error (@lacostej)
* Fix Unity 5.0 being erroneously matched in some cases

#### 0.0.2 (2015-12-21)
* Don’t re-install a version that is already installed (except when using `--install`). This allows to call the script to install a new version or fail if no new version is available
* Improve format of output and add hints on how to resolve errors
* Only ask for permissions once at the beginning
* Move existing installations automatically to not overwrite them or to install additional packages
* Using `--install` doesn’t try to update the cache (for offline installs)
* Specify custom package download directory with `--package-store`
* Check if packages have been downloaded when using `--install`
* Don’t create download directory when not downloading anything
* Add `--all-packaged` to install all available packages
* Fix cache update time not being updated

#### 0.0.1 (2015-12-16)
* Initial release
