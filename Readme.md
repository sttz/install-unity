# install-unity 2

This is the next version of install-unity, a utility to install any recent version of Unity without having to download a separate installer for each.

## What's New

Version 2 is written in C# and AOT-compiled with CoreRT, turning it into a self-contained executable with no dependencies. The CLI is backed by a library, which can be integrated into any .Net project.

The downloading is now parallelized, downloading multiple packages at once and allowing the installation to start while downloads are still in progress. Cursory testing shows the installation to complete 10% - 20% faster.

The biggest additions are the ability to support other platforms than macOS as well as uninstalling and running existing Unity installations. See the [full changelog](#changelog) for the many smaller changes and improvements.

The CLI is not compatible with v1. The interface should feel familiar but has been cleaned up and split into different actions.

## Table of Contents

* [Introduction](#introduction)
* [Versions](#versions)
* [Packages](#packages)
* [Offline Install](#offline-install)
* [Run](#run)
* [CLI Help](#cli-help)
* [Changelog](#changelog)

# Introduction

[Download the latest release here](https://github.com/sttz/install-unity/releases). install-unity is a self-contained executable and has no dependencies.

Installing the latest version of Unity (release or beta) is as simple as:

    install-unity install b

## Versions

Most commands take a version as input, either to select the version to install or to filter the output.

You can be as specific as you like, `2018.2.2f1`, `2018.2.2`, `2018.2`, `2018`, `f` or `2018.3b` are all valid version inputs.

`install-unity` will scan for the available regular releases as well as the latest betas and alphas.

    install-unity list
    install-unity list a
    install-unity list 2019.1

Will show the available versions and the argument acts as a filter. Without an argument, only regular releases are loaded and displayed. Add an argument including `b` or `a` to load and display either beta or both beta and alpha versions as well.

In case install-unity fails to discover a release, it's also possible to pass a release notes or unity hub url instead of a version to `details` and `install`:

    install-unity details https://unity3d.com/unity/whats-new/unity-2018.3.0
    install-unity install unityhub://2018.3.0f2/6e9a27477296

### Patch Releases

With the switch to LTS versions, Unity has stopped creating patch releases for Unity 2017.3 and newer. install-unity no longer scans for patch releases but you can still install them by specifying the full version number.

    install-unity install 2017.2.3p3

## Packages

The command above will install the default packages as specified by Unity.

    install-unity details 2018.2

Will show the available packages for a given version. You can then select the packages you want to install with the `-p` or `--packages` option. The option can either be repeated or the names separated by space or comma:

    install-unity install 2018.2 --packages Unity,Documentation
    install-unity install f -p Unity Linux iOS Android
    install-unity install 2018.3b -p Unity -p Android -p Linux

## Offline Install

install-unity can be used in a two-step process, first downloading the packages and then later installing them without needing an internet connection.

    install-unity install 2018.2 --packages all --data-path "~/Desktop/2018.2" --download

Will download all available packages to `~/Desktop/Downloads` together with the necessary package metadata.

    install-unity install 2018.2 --pacakages all --data-path "~/Destop/2018.2" --install

Will install those packages at a later time. Simply copy the folder together with the `install-unity` binary to another computer to do an offline installation there.

You can download and install only a subset of the available packages.

## Run

To select a Unity version from all the installed ones, use the run command.

    install-unity run --detach f

Will open the latest version of Unity installed.

You can also use the path to a Unity project and install-unity will open it with the corresponding Unity version.

    install-unity run --detach ~/Desktop/my-project

It will only open with the exact version of Unity the project is set to. You can optionally allow it to be opened with a newer patch, minor or any version:

    install-unity run --allow-newer patch ~/Desktop/my-project

You can pass [command line arguments](https://docs.unity3d.com/Manual/CommandLineArguments.html) along to Unity, e.g. to create a build from the command line (note the `--` to separate install-unity options from the ones passed on the Unity).

    install-unity run ~/Desktop/my-project -- -quit -batchmode -buildOSX64Player ~/Desktop/my-build

## CLI Help

````
install-unity v2.3.0

USAGE: install-unity [--help] [--version] [--verbose...] [--yes] [--update] 
                     [--data-path <path>] [--opt <name>=<value>...] <action> 

GLOBAL OPTIONS:
 -h, --help       Show this help 
     --version    Print the version of this program 
 -v, --verbose    Increase verbosity of output, can be repeated 
 -y, --yes        Don't prompt for confirmation (use with care) 
 -u, --update     Force an update of the versions cache 
     --data-path <path>  Store all data at the given path, also don't delete 
                  packages after install 
     --opt <name>=<value>  Set additional options. Use 'list' to show all 
                  options and their default value and 'save' to create an 
                  editable JSON config file. 


---- LIST:
     Get an overview of available or installed Unity versions 

USAGE: install-unity [options] list [--installed] 
                     [--platform none|macos|windows|linux] [<version>] 

OPTIONS:
 <version>        Pattern to match Unity version 
 -i, --installed  List installed versions of Unity 
     --platform none|macos|windows|linux  Platform to list the versions for 
                  (default = current platform) 


---- DETAILS:
     Show version information and all its available packages 

USAGE: install-unity [options] details [--platform none|macos|windows|linux] 
                     [<version>] 

OPTIONS:
 <version>        Pattern to match Unity version or release notes / unity hub 
                  url 
     --platform none|macos|windows|linux  Platform to show the details for 
                  (default = current platform) 


---- INSTALL:
     Download and install a version of Unity 

USAGE: install-unity [options] install [--packages <name,name>...] [--download] 
                     [--install] [--upgrade] 
                     [--platform none|macos|windows|linux] [--yolo] [<version>] 

OPTIONS:
 <version>        Pattern to match Unity version or release notes / unity hub 
                  url 
 -p, --packages <name,name>  Select pacakges to download and install ('all' 
                  selects all available, '~NAME' matches substrings) 
     --download   Only download the packages (requires '--data-path') 
     --install    Install previously downloaded packages (requires 
                  '--data-path') 
     --upgrade    Replace existing matching Unity installation after successful 
                  install 
     --platform none|macos|windows|linux  Platform to download the packages for 
                  (only valid with '--download', default = current platform) 
     --yolo       Skip size and hash checks of downloaded files 


---- UNINSTALL:
     Remove a previously installed version of Unity 

USAGE: install-unity [options] uninstall [<version-or-path>] 

OPTIONS:
 <version-or-path> Pattern to match Unity version or path to installation root 


---- RUN:
     Execute a version of Unity or a Unity project, matching it to its Unity 
     version 

USAGE: install-unity [options] run [--detach] 
                     [--allow-newer none|patch|minor|all] <version-or-path> 
                     [<unity-arguments>...] 

OPTIONS:
 <version-or-path> Pattern to match Unity version or path to a Unity project 
 <unity-arguments> Arguments to launch Unity with (put a -- first to avoid 
                  Unity options being parsed as install-unity options) 
 -d, --detach     Detach from the launched Unity instance 
 -a, --allow-newer none|patch|minor|all  Allow newer versions of Unity to open 
                  a project 
````

# Legacy

The old Python version of install-unity can be found in the [legacy](https://github.com/sttz/install-unity/tree/next) branch.

# Changelog

### 2.3.0 (2019-06-25)

* Indicate installed version with a ✓︎ when using the list action
* Fix using install-unity without a terminal
* Fix EULA prompt defaulting to accept
* Fix release notes URL not shown if there's only one update

### 2.2.0 (2019-05-27)

* Discover all available prerelease versions, including alphas
* Fix `--allow-newer` not working when only the build number is increased (e.g. b1 to b2)
* Fix release notes URL for regular releases
* Indicate new versions in `list` command with ⬆︎
* Increase maximum number of displayed new versions from 5 to 10

### 2.1.1 (2019-02-06)

* Fix automatic detection of beta releases

### 2.1.0 (2018-12-17)

* Use `unityhub://` urls for scraping, fixes discovery of 2018.3.0f2 and 2018.2.20f1
* Allow passing `unityhub://` urls as version for `details` and `install` (like it's already possible with release notes urls)
* Now `--upgrade` selects the next older installed version to remove, relative to the version being installed. Previously it would use the version pattern, which didn't work when using an exact version or an url.

### 2.0.1 (2018-12-10)

* Add `--yolo` option to skip size and hash checks (required sometimes when Unity gets them wrong)
* Fix package added twice when dependency has been selected manually
* Fix exception when drawing progress bar
* Minor output fixes

### 2.0.0 (2018-11-13)

* Install alphas using their full version number or their release notes url
* Fix scraping of beta releases

### 2.0.0-beta3 (2018-10-27)

* Accept url to release notes as version argument in `install` and `details`
* Fix guessed release notes url for regular Unity releases
* Add message when old Unity version is removed during an upgrade to avoid the program to appear stalled
* Small visual tweaks to progress output

### 2.0.0-beta2 (2018-10-01)

* Add `--upgrade` to `install` to replace existing version after installation
* Don't update outdated cache when using `list --installed`

### 2.0.0-beta1 (2018-08-13)

* Rewritten as a library in C#
* Improved command line interface and output
* Faster installs thanks to parallelization
* List installed versions and uninstall them
* Substring package name matching (using `~NAME`)
* Automatic selection of dependent packages
* Better cleanup of aborted installs
* Support for differentiating versions based on build hash
* Retry downloads
* Support for installing DMGs on macOS (Visual Studio for Mac)
* Discover installations outside of `/Applications` on macOS
* Select and run Unity with command line arguments
* Patch releases only supported with full version number
* Still a single executable without dependencies
* Planned Windows and Linux support (help welcome)
