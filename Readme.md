# Install Unity Script

Unofficial Unity 3D installer for OS X.

# Requirements & Installation

The Install Unity Script runs without additional dependencies on all versions of OS X with Python 2.7 (apparently 10.7+).

To use, simply download the script and run it from the command line:<br>
`./install-unity.py --help`

# Introduction

Unity has begun to split their engine into different packages that can be installed separately and offer installers that allow to download and install only the desired packages.

The Install Unity Script taps into this infrastructure and allows to quickly download and install different versions of Unity, without having to download a different installer for each version.

e.g. installing multiple 5.x versions at once can be done using the following command:<br>
`./install-unity.py --package Unity 5.0 5.1 5.2 5.3`

This will install only the Unity editor, with no additional packages. The script will detect existing installations in folders starting with "Unity" in the Applications folder and will temporarily move them to install new versions or additional packages for existing versions.

Later, additional packages can be installed (platform packages only available with Unity 5.3+), e.g:<br>
`./install-unity.py --package Mac --package Windows --package Linux 5.3`

# Available Versions

Unity is packaged this way starting from Unity 5.0, the install script doesn’t support earlier versions.

The install script scans public Unity HTML pages and does not discover all versions available. For regular releases, this includes all versions of Unity 5.x.x. For patch releases, this includes only the newest 5 and for beta releases only the ones for the upcoming Unity version (if any).

Versions can be added manually by finding the URL to the Mac editor installer containing a 12-character hash code, e.g. `http://netstorage.unity3d.com/unity/2524e04062b4/MacEditorInstaller/Unity-5.3.0f4.pkg` and by calling:<br>
`./install-unity.py --discover URL`

# Selecting Versions

Versions can be specified with arbitrary precision, the install script will then select the latest available version that matches.

E.g. “5” will select the latest version of Unity 5, “5.3” the latest version of Unity 5.3 and “5.2.3” the latest version of Unity 5.2.3.

If no release type is specified, only regular (f) releases will be installed. Add p for patch, b for beta or a for alpha to any version to select another release type. If no releases of a specific type are known, other types will be checked in the following order: alpha —> beta —> patch —> release

E.g. “5.3p” will install the latest patch or the latest regular release for Unity 5.3. “5.4a” will install the latest Unity 5.4 release, be it alpha, beta, patch or regular.

# Selecting Packages

Some of the packages are only available in later versions of Unity. Prior to Unity 5.3, the main Unity editor installer includes all supported platforms and they cannot be installed separately.

Use the following command to show all available packages for a given version:<br>
`./install-unity.py --list VERSION`

If no package is specified, the default packages (same as in the official Unity installer) will be installed. Otherwise, any number of `-p PACKAGE` or `--package PACKAGE` can be specified to select packages, selected packages that are not available for a given version will be ignored.

# Offline Installation

The Unity install script can download and install the packages separately, allowing you to install Unity on multiple computers while only downloading the packages once.

First, download the packages using the `--download` flag. By default, the packages are downloaded to `~/Downloads` but you can set a custom download path using `--package-store`. Execute the following command in the script directory to download all available packages into the script directory, so you only need to copy a single folder to the computer you want to install Unity on:<br>
`./install-unity.py --download --all-packages --package-store . VERSION`

This will create a `Unity Packages` folder inside the script directory that contains all downloaded packages, sorted by version. Copy the folder with the script, the `unity_versions.json` and all `unity-*-osx.ini` files to the target computer and then call:<br>
`./install-unity.py --install --all-packages --package-store . VERSION`

Instead of installing all packages, you can select which packages to install using mutliple `--package` flags. You can also specify multiple versions to install different Unity versions at once.

# Commands

All available commands:
```
usage: install-unity.py [-h] [--version] [--list] [--download] [--install]
                        [--volume VOLUME] [-p PACKAGE] [--all-packages]
                        [--package-store PACKAGE_STORE] [-k] [-u]
                        [--list-versions {release,patch,all}]
                        [--discover DISCOVER] [--forget FORGET]
                        [VERSION [VERSION ...]]

Install Unity Script 0.0.2

positional arguments:
  VERSION               unity version to install packages from (only >= 5.0.0)

optional arguments:
  -h, --help            show this help message and exit
  --version             show program's version number and exit
  --list                only list available packages
  --download            only download the version(s), don't install them
  --install             only install the version(s), they must have been
                        downloaded previously
  --volume VOLUME       set the target volume (must be a volume mountpoint)
  -p PACKAGE, --package PACKAGE
                        add package to download or install, absent = install
                        default packages
  --all-packages        install all packages instead of only the default ones
                        when no packages are selected
  --package-store PACKAGE_STORE
                        location where the downloaded packages are stored
                        (temporarily, if not --download or --keep)
  -k, --keep            don't remove installer files after installation
                        (implied when using --install)
  -u, --update          force updating of cached version information
  --list-versions {release,patch,all}
                        list the cached unity versions
  --discover DISCOVER   manually discover a Unity packages url (link to unity-
                        VERSION-osx.ini or MacEditorInstaller url)
  --forget FORGET       remove a manually discovered version
```
