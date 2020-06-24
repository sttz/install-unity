# install-unity

A command-line utility to install any recent version of Unity.

Currently only supports macOS but support for Windows/Linux is possible, PRs welcome.

## Table of Contents

* [Introduction](#introduction)
* [Versions](#versions)
* [Packages](#packages)
* [Offline Install](#offline-install)
* [Run](#run)
* [Create](#create)
* [CLI Help](#cli-help)
* [Changelog](#changelog)

# Introduction

[Download the latest release here](https://github.com/sttz/install-unity/releases). install-unity is a self-contained executable and has no dependencies.

Or you can install via [Homebrew](https://brew.sh) using [sttz/homebrew-tap](https://github.com/sttz/homebrew-tap), see the tap readme for instructions.

Installing the latest release version of Unity is as simple as:

    install-unity install f

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

    install-unity run f

Will open the latest version of Unity installed.

You can also use the path to a Unity project and install-unity will open it with the corresponding Unity version.

    install-unity run ~/Desktop/my-project

It will only open with the exact version of Unity the project is set to. You can optionally allow it to be opened with a newer patch, minor or any version:

    install-unity run --allow-newer patch ~/Desktop/my-project

You can pass [command line arguments](https://docs.unity3d.com/Manual/CommandLineArguments.html) along to Unity, e.g. to create a build from the command line (note the `--` to separate install-unity options from the ones passed on the Unity).

    install-unity run ~/Desktop/my-project -- -quit -batchmode -buildOSX64Player ~/Desktop/my-build

By default, Unity is started as a separate process and install-unity will exit after Unity has been launched. To wait for Unity to quit and forward Unity's log output through install-unity, use the `--child` option:

    install-unity run ~/Desktop/my-project --child -v -- -quit -batchmode -buildOSX64Player ~/Desktop/my-build

## Create

To start a basic Unity project, use the create command. The version pattern will select an installed Unity version and create a new project using it.

    install-unity create 2020.1 ~/Desktop/my-project

The project will use Unity's default setup, including packages. Alternatively, you can create a minimal project that will start with an empty ´Packages/manifest.json`:

    install-unity create --type minimal 2020.1 ~/Desktop/my-project

## CLI Help

````
install-unity v2.8.0

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
 -p, --packages <name,name>  Select packages to download and install ('all' 
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

USAGE: install-unity [options] run [--child] 
                     [--allow-newer none|hash|build|patch|minor|all] 
                     <version-or-path> [<unity-arguments>...] 

OPTIONS:
 <version-or-path> Pattern to match Unity version or path to a Unity project 
 <unity-arguments> Arguments to launch Unity with (put a -- first to avoid 
                  Unity options being parsed as install-unity options) 
 -c, --child      Run Unity as a child process and forward its log output (only 
                  errors, use -v to see the full log) 
 -a, --allow-newer none|hash|build|patch|minor|all  Allow newer versions of 
                  Unity to open a project 


---- CREATE:
     Create a new empty Unity project 

USAGE: install-unity [options] create [--type <basic|minimal>] [--open] 
                     <version> <path> 

OPTIONS:
 <version>        Pattern to match the Unity version to create the project with 
 <path>           Path to the new Unity project 
     --type <basic|minimal>  Type of project to create (basic = standard 
                  project, minimal = no packages/modules) 
 -o, --open       Open the new project in the editor 
````

# Legacy

The old Python version of install-unity can be found in the [legacy](https://github.com/sttz/install-unity/tree/next) branch.
