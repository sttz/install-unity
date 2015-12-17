# Unity Install Manager

Unofficial Unity 3D installer for OS X.

# Requirements & Installation

The Unity Install Manager runs without additional dependencies on all versions of OS X with Python 2.7 (apparently 10.7+).

To use, simply download the script and run it from the command line:
`python unity-install-manager.py —help`

# Introduction

Unity has begun to split their engine into different packages that can be installed separately and offer installers that allow to download and install only the desired packages.

The Unity Install Manager taps into this infrastructure and allows to quickly download and install different versions of Unity, without having to download a different installer for each version.

e.g. installing all major 5.x versions at once can be done using the following command:
`python unity-install-manager.py —package Unity 5.0 5.1 5.2 5.3`

This will install only the Unity editor, with no additional packages. The installer will automatically handle the installs so the different versions are installed side by side.

Later, additional packages can be installed (only available with Unity 5.3+), e.g:
`python unity-install-manager.py —package Mac —package Windows —package Linux 5.3`

# Available Versions

Unity is packaged this way only starting from Unity 5.0, the install manager doesn’t support earlier versions.

The install manager scans public Unity HTML pages and does not discover all versions available. Specifically, it only scans the latest page of patch and no beta releases.

Versions can be added manually by finding the URL to the Mac editor installer containing 12-character hash code, e.g. `http://netstorage.unity3d.com/unity/2524e04062b4/MacEditorInstaller/Unity-5.3.0f4.pkg` and by calling:
`python unity-install-manager.py —discover URL`

# Selecting Versions

Versions can be specified with arbitrary precision, the install manager will then select the latest available version that matches.

E.g. “5” will select the latest version of Unity 5, “5.3” the latest version of Unity 5.3 and “5.2.3” the latest version of Unity 5.2.3, including patch releases.

To ignore patch releases, an “f” can be appended to the version, e.g. “5.3f” will select the latest non-patch release of Unity 5.3.

# Selecting Packages

Some of the packages are only available in later Unity versions. Prior to Unity 5.3, the main Unity editor installer includes all supported platforms and they cannot be installed separately.

Use the following command to show all available packages for a given version:
`python unity-install-manager.py —list VERSION`

If no package is specified, all available packages will be installed. Otherwise, any number of `-p PACKAGE` or `—package PACKAGE` can be specified to select packages, selected packages that are not available for a given version will be ignored.

# Commands

All available commands:
‘’’
usage: unity-install-manager.py [-h] [—version] [—list] [—download]
                                [—install] [—volume VOLUME] [-p PACKAGE]
                                [-k] [-u]
                                [—list-versions {release,patch,all}]
                                [—discover DISCOVER] [—forget FORGET]
                                [VERSION [VERSION …]]

Unity Installation Manager 0.0.1

positional arguments:
  VERSION               unity version to install packages from (only >= 5.0.0)

optional arguments:
  -h, —help            show this help message and exit
  —version             show program’s version number and exit
  —list                only list available packages
  —download            only download the version(s), don’t install them
  —install             only install the version(s), they must have been
                        downloaded previously
  —volume VOLUME       set the target volume (must be a volume mountpoint)
  -p PACKAGE, —package PACKAGE
                        add package to download or install, default is to
                        install all available
  -k, —keep            don’t remove installer files after installation
  -u, —update          force updating of cached version information
  —list-versions {release,patch,all}
                        list the cached unity versions
  —discover DISCOVER   manually discover a Unity packages url (link to unity-
                        VERSION-osx.ini or MacEditorInstaller url)
  —forget FORGET       remove a manually discovered version
‘’’