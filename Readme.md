# Install Unity v2

This is the next version of Install Unity, a script to install any recent version of Unity without having to download a separate installer for each.

### What's New In v2

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
* Patch releases only supported with full version number
* Still a single executable without dependencies
* Planned Windows and Linux support (help welcome)

# Introduction

Download the latest release here. Install Unity is a self-contained executable and has no dependencies.

Installing the latest version of Unity (release or beta) is a simple as:

    install-unity install b

### Versions

Most commands take a version as input, either to select the version to install or to filter the output.

You can be as specific as you like, `2018.2.2f1`, `2018.2.2`, `2018.2`, `2018`, `f` or `2018.3b` are all valid version inputs.

`install-unity` will scan for the available regular releases and the latest betas.

    install-unity list

Will show the available versions.

### Packages

The command above will install the default packages as specified by Unity.

    install-unity details 2018.2

Will show the available packages for a given version. You can then select the packages you want to install with the `-p` or `--packages` options. The option can either be repeated or the names separated by space or comma:

    install-unity install 2018.2 --packages Unity,Documentation
    install-unity install f -p Unity Linux iOS Android
    install-unity install 2018.3b -p Unity -p Android -p Linux

### Offline Install

Install Unity can be used in a two-step process, first downloading the packages and then later installing them without needing an internet connection.

    install-unity install 2018.2 --packages all --data-path "~/Desktop/2018.2" --download

Will download all available packages to "~/Desktop/Downloads" together with the necessary package metadata.

    install-unity install 2018.2 --pacakages all --data-path "~/Destop/2018.2" --install

Will install those packages at a later time. Simply copy the folder together with the Install Unity binary to another computer to do an offline installation there.

You can download and install only a subset of the available packages.

### Patch Releases

With the switch to LTS versions, Unity has stopped creating patch releases for newer versions. Install Unity no longer discovers patch releases but you can still install them by specifying the full version number.

    install-unity install 2017.2.3p3

# Release Notes

### 2.0.0 (2018-08-xx)

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
* Patch releases only supported with full version number
* Still a single executable without dependencies
* Planned Windows and Linux support (help welcome)
