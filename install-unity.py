#!/usr/bin/python
# -*- coding: utf-8 -*-

# The MIT License (MIT)
# 
# Copyright (c) 2015 Adrian Stutz
# 
# Permission is hereby granted, free of charge, to any person obtaining a copy
# of this software and associated documentation files (the "Software"), to deal
# in the Software without restriction, including without limitation the rights
# to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the Software is
# furnished to do so, subject to the following conditions:
# 
# The above copyright notice and this permission notice shall be included in all
# copies or substantial portions of the Software.
# 
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
# IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
# FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
# AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
# LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
# OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
# SOFTWARE.

import argparse
import collections
import ConfigParser
import datetime
import dateutil.parser
import getpass
import hashlib
import io
import json
import math
import os
import pipes
import re
import shutil
import subprocess
import sys
import time
import traceback
import urllib
import urllib2

# ---- CONFIGURATION ----

VERSION = '0.0.2'

# URL to look for main Unity releases
UNITY_DOWNLOADS = 'http://unity3d.com/get-unity/download/archive'
# URL to look for Unity patch releases
UNITY_PATCHES = 'http://unity3d.com/unity/qa/patch-releases'
# URL to look for beta releases
UNITY_BETAS = 'https://unity3d.com/unity/beta/archive'
# Regex to find relative beta page URI from HTML
UNITY_BETAVERSION_RE = '"/unity/beta/unity(\d+\.\d+\.\d+\w\d+)"'
# parametrized beta version URL, given its version
UNITY_BETAVERSION_URL = "https://unity3d.com/unity/beta/unity%s"
# Regex to parse package URLs from HTML
UNITY_DOWNLOADS_RE = '"(https?:\/\/[\w\/.-]+\/[0-9a-f]{12}\/)MacEditorInstaller\/[\w\/.-]+(\d+\.\d+\.\d+\w\d+)[\w\/.-]+"'

# Name of the ini file at the package URL that contains package information (%s = version)
UNITY_INI_NAME = 'unity-%s-osx.ini'
# Regex to parse URLs given to --discover
UNITY_INI_RE = '(https?:\/\/[\w\/.-]+\/[0-9a-f]{12}\/)[\w\/.-]+(\d+\.\d+\.\d+\w\d+)[\w\/.-]+'

# Name of the Unity versions cache (created from above URLs)
CACHE_FILE = 'unity_versions.json'
# Lifetime of the cache, use --update to force an update
CACHE_LIFETIME = 60*60*24

# Regex to parse Unity versions in the format of e.g. '5.3.2p3'"
VERSION_RE = '^(\d+)(?:\.(\d+)(?:\.(\d+))?)?(?:(\w)(?:(\d+))?)?$'
# Unity release types and corresponding letters in version string
RELEASE_LETTERS = { 'release': 'f', 'patch': 'p' }
# Sorting power of unity release types
RELEASE_LETTER_STRENGTH = { 'f': 1, 'p': 2, 'b': 3, 'a': 4 }

# Default location where downloaded packages are temporarily stored
# (Unless --download, --install or --keep is used, in which case they are not removed)
DOWNLOAD_PATH = '~/Downloads/'
# Name of top directory packages are stored (in subdirectories by version)
DOWNLOAD_DIRECTORY = 'Unity Packages'

# ---- ARGUMENTS ----

parser = argparse.ArgumentParser(description='Install Unity Script ' + VERSION)
parser.add_argument('--version', action='version', version='%(prog)s ' + VERSION)

parser.add_argument('versions', 
    metavar='VERSION', type=str, nargs='*',
    help='unity version to install packages from (only >= 5.0.0)')

parser.add_argument('--list', 
    action='store_const', const='list', dest='operation',
    help='only list available packages')
parser.add_argument('--download', 
    action='store_const', const='download', dest='operation',
    help='only download the version(s), don\'t install them')
parser.add_argument('--install', 
    action='store_const', const='install', dest='operation',
    help='only install the version(s), they must have been downloaded previously')

parser.add_argument('--volume', 
    default='/',
    help='set the target volume (must be a volume mountpoint)')
parser.add_argument('-p', '--package', 
    action='append',
    help='add package to download or install, absent = install default packages')
parser.add_argument('--all-packages', 
    action='store_true',
    help='install all packages instead of only the default ones when no packages are selected')
parser.add_argument('--package-store', 
    action='store',
    help='location where the downloaded packages are stored (temporarily, if not --download or --keep)')
parser.add_argument('-k', '--keep', 
    action='store_true',
    help='don\'t remove installer files after installation (implied when using --install)')

parser.add_argument('-u', '--update', 
    action='store_true',
    help='force updating of cached version information')
parser.add_argument('--list-versions', 
    choices=['release', 'patch', 'all'],
    help='list the cached unity versions')
parser.add_argument('--discover', 
    action='append',
    help='manually discover a Unity packages url (link to unity-VERSION-osx.ini or MacEditorInstaller url)')
parser.add_argument('--forget', 
    action='append',
    help='remove a manually discovered version')

args = parser.parse_args()

# ---- GENERAL ----

def error(message):
    print 'ERROR: ' + message
    traceback.print_stack()
    sys.exit(1)

# ---- VERSIONS CACHE ----

class version_cache:
    def __init__(self, cache_path, update = None):
        self.cache_path = cache_path
        self.cache_file = os.path.join(cache_path, CACHE_FILE)
        
        self.cache = {}
        self.sorted_versions = None
        
        self.load()
        
        need_update = False
        if update is True:
            print "Forcing an update of Unity versions list..."
            need_update = True
        elif update is None:
            if not 'lastupdate' in self.cache:
                print "No cache found, updating Unity versions list..."
                need_update = True
            else:
                lastupdate = dateutil.parser.parse(self.cache['lastupdate'])
                if (datetime.datetime.utcnow() - lastupdate).total_seconds() > CACHE_LIFETIME:
                    print "Cache outdated, updating Unity versions list..."
                    need_update = True
        
        if need_update:
            self.update()
    
    def load(self):
        if not os.path.isfile(self.cache_file):
            return
        
        with open(self.cache_file, 'r') as file:
            data = file.read()
            self.cache = json.loads(data)
            self.sorted_versions = None
    
    def update(self):
        self.cache['versions'] = {}
        self.cache['lastupdate'] = datetime.datetime.utcnow().isoformat()
        
        print 'Loading Unity releases...'
        count = self._load_and_parse(UNITY_DOWNLOADS, UNITY_DOWNLOADS_RE, self.cache['versions'])
        if count > 0: print 'Found %i Unity releases.' % count
        
        print 'Loading Unity patch releases...'
        count = self._load_and_parse(UNITY_PATCHES, UNITY_DOWNLOADS_RE, self.cache['versions'])
        if count > 0: print 'Found %i Unity patch releases.' % count
        
        print 'Loading Unity beta releases...'
        count = self._load_and_parse_betas(UNITY_BETAS, UNITY_DOWNLOADS_RE, self.cache['versions'])
        if count > 0: print 'Found %i Unity patch releases.' % count

        print ''
        
        self.save()
        self.sorted_versions = None
    
    def _load_and_parse_betas(self, url, pattern, unity_versions):
        try:
            response = urllib2.urlopen(url)
        except Exception as e:
            error('Could not load URL "%s": %s' % url, e.reason)

        result = sorted(set(re.findall(UNITY_BETAVERSION_RE, response.read())))
        for betaversion in result:
            versionurl = UNITY_BETAVERSION_URL % betaversion
            self._load_and_parse(versionurl, pattern, unity_versions)

        return len(result)

    def _load_and_parse(self, url, pattern, unity_versions):
        try:
            response = urllib2.urlopen(url)
        except Exception as e:
            error('Could not load URL "%s": %s' % url, e.reason)
        
        result = re.findall(pattern, response.read())
        for match in result:
            unity_versions[match[1]] = match[0]
        return len(result)
    
    def save(self):
        with open(self.cache_file, 'w') as file:
            data = json.dumps(self.cache)
            file.write(data)
    
    def add(self, url):
        result = re.search(UNITY_INI_RE, url)
        if result is None:
            print 'WARNING: Could not parse Unity packages url: %s' % url
            return None
        
        baseurl = result.group(1)
        version = result.group(2)
        
        ini_name = UNITY_INI_NAME % version
        
        ini_url = baseurl + ini_name
        success = False
        try:
            urllib2.urlopen(ini_url)
        except urllib2.HTTPError, e:
            print 'ERROR: Failed to load url "%s", returned error code %d' % (ini_url, e.code)
        except urllib2.URLError, e:
            print 'ERROR: Failed to load url "%s", error: %s' % (ini_url, e.reason)
        else:
            success = True
        
        if not success: return None
        
        if not 'discovered' in self.cache:
            self.cache['discovered'] = {}
        
        self.cache['discovered'][version] = baseurl
        self.sorted_versions = None
        return version
    
    def remove(self, version):
        if not 'discovered' in self.cache or not version in self.cache['discovered']:
            print "WARNING: Version %s not found in manually discovered versions" % versions
            return False
        
        del self.cache['discovered'][version]
        self.sorted_versions = None
        return True
    
    def get_baseurl(self, version):
        if 'discovered' in self.cache and version in self.cache['discovered']:
            return self.cache['discovered'][version]
        elif version in self.cache['versions']:
            return self.cache['versions'][version]
        else:
            return None
    
    def get_sorted_versions(self):
        if self.sorted_versions == None:
            all_versions = self.cache['versions'].keys()
            if 'discovered' in self.cache:
                all_versions += self.cache['discovered'].keys()
            self.sorted_versions = sorted(all_versions, compare_versions)
        
        return self.sorted_versions
    
    def list(self, type):
        letter = None
        if type:
            letter = RELEASE_LETTERS[type]
        
        print 'Known available Unity versions:'
        print '(Use "--discover URL" to add versions not automatically discovered)'
        for version in self.get_sorted_versions():
            if letter and not letter in version:
                continue
            print '- %s' % version

# ---- VERSION HANDLING ----

def parse_version(version):
    match = re.match(VERSION_RE, version)
    if not match:
        error('Version %s does not conform to Unity version format 0.0.0x0' % version)
    
    parts = list(match.groups())
    
    # Convert to int, except fourth element wich is release type letter
    for i in range(len(parts)):
        if not parts[i] or i == 3: continue
        parts[i] = int(parts[i])
    
    if parts[3]:
        if not parts[3] in RELEASE_LETTER_STRENGTH:
            error('Unknown release letter "%s" from "%s"' % (parts[3], version))
        parts[3] = RELEASE_LETTER_STRENGTH[parts[3]]
    else:
        parts[3] = RELEASE_LETTER_STRENGTH['f']
    
    return parts

def compare_versions(one, two):
    return cmp(parse_version(one), parse_version(two))

def match_version(one, two):
    for i in range(5):
        if one[i] and two[i] and one[i] != two[i]:
            return False
    return True

def select_version(version, sorted_versions):
    one = parse_version(version)
    
    for i in reversed(range(len(sorted_versions))):
        two = parse_version(sorted_versions[i])
        if match_version(one, two):
            if version != sorted_versions[i]:
                print 'Selected version %s for input version %s' % (sorted_versions[i], version)
            return sorted_versions[i]
    
    error('Version %s is now a known Unity version' % version)

# ---- DOWNLOAD ----

def convertSize(size):
    size = size / 1024
    size_name = ("KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB")
    i = int(math.floor(math.log(size,1024)))
    p = math.pow(1024,i)
    s = round(size/p,2)
    if (s > 0):
        return '%s %s' % (s,size_name[i])
    else:
        return '0B'

def download_url(url, output):
    print ""
    
    urllib.urlretrieve(url, output, progress)
    
    sys.stdout.write("\033[F")
    sys.stdout.write("\033[K")

block_times = None
last_update = None

def progress(blocknr, blocksize, size):
    global block_times, last_update
    
    if blocknr == 0:
        block_times = collections.deque()
        block_times.append(time.time())
        last_update = 0
        return
    
    if time.time() - last_update > 0.5:
        last_update = time.time()
        
        window_duration = time.time() - block_times[0]
        window_size = len(block_times) * blocksize
        speed = window_size / window_duration
    
        size_done = blocknr * blocksize
        current = min(1.0, size_done / float(size))
    
        sys.stdout.write("\033[F")
        sys.stdout.write("\033[K")
    
        sys.stdout.write('[')
        sys.stdout.write('=' * int(math.floor(current * 60)))
        sys.stdout.write('>')
        sys.stdout.write('·' * int(math.ceil((1 - current) * 60) - 1))
        sys.stdout.write('] ')
        sys.stdout.write('{0:.2f}% | '.format(100.0 * current))
        sys.stdout.write('{0}/s '.format(convertSize(speed)))
        sys.stdout.write('\n')
    
    block_times.append(time.time())
    if (len(block_times) > 100):
        block_times.popleft()

def hashfile(path, blocksize=65536):
    with open(path, 'rb') as file:
        hasher = hashlib.md5()
        buf = file.read(blocksize)
        while len(buf) > 0:
            hasher.update(buf)
            buf = file.read(blocksize)
        return hasher.hexdigest()

def load_ini(version):
    baseurl = cache.get_baseurl(version)
    if not baseurl:
        error('Version %s is now a known Unity version' % version)
    
    ini_name = UNITY_INI_NAME % version
    ini_path = os.path.join(script_dir, ini_name)
    
    if not os.path.isfile(ini_path):
        url = baseurl + ini_name
        try:
            response = urllib2.urlopen(url)
        except Exception as e:
            error('Could not load URL "%s": %s' % url, e.reason)
    
        with open(ini_path, 'w') as file:
            file.write(response.read())
    
    config = ConfigParser.ConfigParser()
    config.read(ini_path)
    return config

def select_packages(config, packages):
    available = config.sections()
    
    if len(packages) == 0:
        if args.all_packages:
            selected = available
        else:
            selected = [x for x in available if config.getboolean(x, 'install')]
    else:
        # ConfigParser sections are case-sensitive, make sure
        # we use the proper case regardless what the user entered
        lower_to_upper = {}
        for pkg in available:
            lower_to_upper[pkg.lower()] = pkg
        
        selected = []
        for select in packages:
            if select.lower() in lower_to_upper:
                selected.append(lower_to_upper[select.lower()])
            else:
                print 'WARNING: Unity version %s has no package "%s"' % (version, select)
    
    # If the main Unity editor package was selected, 
    # make sure it's installed first
    if 'Unity' in selected:
        selected.remove('Unity')
        selected.insert(0, 'Unity')
    
    return selected

def download(version, path, config, selected):
    print 'Will download %s in total' % convertSize(sum(map(lambda pkg: config.getint(pkg, 'size'), selected)))
    
    if not os.path.isdir(path):
        os.makedirs(path)
    
    for pkg in selected:
        if not config.has_option(pkg, 'md5'):
            print 'WARNING: Cannot verify file "%s": No md5 hash found.' % filename
            md5hash = None
        else:
            md5hash = config.get(pkg, 'md5')
        
        baseurl = cache.get_baseurl(version)
        fileurl = baseurl + config.get(pkg, 'url')
        filename = os.path.basename(fileurl)
        output = os.path.join(path, filename)
        
        if os.path.isfile(output) and md5hash and hashfile(output) == md5hash:
            print 'File %s already downloaded' % filename
        else:
            print 'Downloading %s (%s)...' % (filename, convertSize(config.getint(pkg, 'size')))
            download_url(fileurl, output)
        
        if md5hash and hashfile(output) != md5hash:
            error('Downloaded file "%s" is corrupt, hash does not match.' % filename)
    
    print 'Download complete!'
    print ''

# ---- INSTALL ----

def find_unity_installs():
    installs = {}
    
    app_dir = os.path.join(args.volume, 'Applications')
    if not os.path.isdir(app_dir):
        error('Applications directory on target volume "%s" not found' % args.volume)
    
    install_paths = [x for x in os.listdir(app_dir) if x.startswith('Unity')]
    for install_name in install_paths:
        plist_path = os.path.join(app_dir, install_name, 'Unity.app', 'Contents', 'Info.plist')
        if not os.path.isfile(plist_path):
            print "WARNING: No Info.plist found at '%s'" % plist_path
            continue
        
        installed_version = subprocess.check_output(['defaults', 'read', plist_path, 'CFBundleVersion']).strip()
        
        installs[installed_version] = os.path.join(app_dir, install_name)
    
    if len(installs) == 0:
        print "No existing Unity installations found."
    else:
        print 'Found %d existing Unity installations:' % len(installs)
        for install in installs:
            print '- %s (%s)' % (install, installs[install])
    print ''
    
    return installs

def install(version, path, selected):
    missing = False
    for pkg in selected:
        filename = os.path.basename(config.get(pkg, 'url'))
        if not os.path.isfile(os.path.join(path, filename)):
            print 'Package "%s" has not been downloaded' % filename
            missing = True
    
    if missing:
        error('Some packages to be installed have not been downloaded')
    
    if not version in installs and not 'Unity' in selected:
            error('Installing only components but no matching Unity %s installation found' % version)
    
    install_path = os.path.join(args.volume, 'Applications', 'Unity')
    
    moved_unity_to = None
    if version in installs and os.path.basename(installs[version]) == 'Unity':
        # The 'Unity' folder already contains the target version
        pass
    elif os.path.isdir(install_path):
        # There's another version in the 'Unity' folder, move it to 'Unity VERSION'
        lookup = [vers for vers,name in installs.iteritems() if os.path.basename(name) == 'Unity']
        if len(lookup) != 1:
            error('Directory "%s" not recognized as Unity installation.' % install_path)
        
        moved_unity_to = os.path.join(args.volume, 'Applications', 'Unity %s' % lookup[0])
        if os.path.isdir(moved_unity_to):
            error('Duplicate Unity installs in "%s" and "%s"' % (install_path, moved_unity_to))
        
        os.rename(install_path, moved_unity_to)
    
    # If a matching version exists elsewhere, move it to 'Unity'
    moved_unity_from = None
    if version in installs and os.path.basename(installs[version]) != 'Unity':
        moved_unity_from = installs[version]
        os.rename(moved_unity_from, install_path)
    
    try:
        for pkg in selected:
            filename = os.path.basename(config.get(pkg, 'url'))
            package = os.path.join(path, filename)
            
            print 'Installing %s...' % filename
            
            command = 'echo "%s" | /usr/bin/sudo -S /usr/sbin/installer -pkg %s -target %s -verbose' % (pwd, pipes.quote(package), pipes.quote(args.volume))
            subprocess.check_output(command, shell=True, stderr=subprocess.STDOUT)
    except subprocess.CalledProcessError as e:
        error('Installation of package "%s" failed: %s' % (filename, e.output))
    finally:
        # Revert moving around Unity installations
        if moved_unity_from:
            os.rename(install_path, moved_unity_from)
        
        if moved_unity_to:
            if os.path.isdir(install_path):
                # If there previously was a 'Unity' folder, move the newly
                # installed Unity to 'Unity VERSION'
                new_install_path = os.path.join(args.volume, 'Applications', 'Unity %s' % version)
                os.rename(install_path, new_install_path)
            os.rename(moved_unity_to, install_path)
    
    print 'Installation complete!'
    print ''

def clean_up(path):
    # Prevent cleanup if there are unexpected files in the download directory
    for file in os.listdir(path):
        file_lower = file.lower()
        if not file_lower.endswith('.pkg') and not file == '.DS_Store':
            print 'WARNING: Cleanup aborted because of unkown file "%s" in "%s"' % (file, path)
            return
    
    shutil.rmtree(path)
    
    downloads = os.path.expanduser(download_to)
    
    for file in os.listdir(downloads):
        if not file == '.DS_Store':
            return
    
    shutil.rmtree(downloads)

# ---- MAIN ----

print 'Install Unity Script %s\n' % VERSION

script_dir = os.path.dirname(os.path.abspath(__file__))
operation = args.operation
packages = [x.lower() for x in args.package] if args.package else []

# When --update is set we also clear all ini files to force re-downloading them
if args.update:
    for file in os.listdir(script_dir):
        if file.startswith("unity") and file.endswith(".ini"):
            os.remove(os.path.join(script_dir, file))

# Setup version cache, handle adding and removing of versions
update_cache = None
if args.update:
    update_cache = True
elif operation == 'install':
    update_cache = False

cache = version_cache(script_dir, update_cache)

if args.discover or args.forget:
    if args.forget:
        for version in args.forget:
            if cache.remove(version):
                print 'Removed version %s from cache' % version
    if args.discover:
        for url in args.discover:
            version = cache.add(url)
            if version:
                print 'Added version %s to cache' % version
    cache.save()
    print ''

if args.list_versions or len(args.versions) == 0:
    operation = 'list-versions'

if not operation or operation == 'install':
    # Get the root password early so we don't need to ask for it
    # after the downloads potentially took a long time to finish.
    # Also, just calling sudo might expire when the install takes
    # long and the user would have to enter his password again 
    # and again.
    print 'Your admin password is required to install the packages'
    pwd = getpass.getpass('User password:')
    
    # Check the root password, so that the user won't only find out
    # much later if the password is wrong
    command = 'sudo -k && echo "%s" | /usr/bin/sudo -S /usr/bin/whoami' % pwd
    result = subprocess.call(command, shell=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result != 0:
        error('User password invalid or user not an admin')
    
    print ''

# Download path
download_to = args.package_store
if not download_to:
    download_to = DOWNLOAD_PATH

download_to = os.path.expanduser(os.path.join(download_to, DOWNLOAD_DIRECTORY))

# Main Operation
if operation == 'list-versions':
    find_unity_installs()
    
    cache.list(args.list_versions)
    
    print ''
    print 'List available packages for a given version using "--list VERSION"'

else:
    installs = find_unity_installs()
    sorted_installs = sorted(installs.keys(), compare_versions)
    
    if operation == 'list' or len(packages) == 0 or 'unity' in packages:
        print 'Trying to select most recent known Unity version'
        version_list = cache.get_sorted_versions()
    else:
        print 'Installing additional packages ("Unity" editor package not selected)'
        
        if len(sorted_installs) == 0:
            error('No Unity installation found, intall the "Unity" editor package first')
        
        print 'Trying to select the most recent installed version'
        version_list = sorted_installs
    
    versions = set([select_version(v, version_list) for v in args.versions])
    print ''
    
    for version in versions:
        config = load_ini(version)
        
        if operation == 'list':
            print 'Available packages for Unity version %s:' % version
            for pkg in config.sections():
                print '- %s%s (%s)' % (
                    pkg, 
                    '*' if config.getboolean(pkg, 'install') else '', 
                    convertSize(config.getint(pkg, 'size'))
                )
            print ''
        else:
            path = os.path.expanduser(os.path.join(download_to, version))
            
            print 'Processing packages for Unity version %s:' % version
            
            selected = select_packages(config, packages)
            if len(selected) == 0:
                print 'WARNING: No packages selected for version %s' % version
                continue
            
            print 'Selected packages: %s' % ", ".join(selected)
            print ''
            
            if not operation and 'Unity' in selected and version in installs:
                print 'WARNING: Unity version %s already installed at "%s", skipping.' % (version, installs[version])
                print 'Don\'t select the Unity editor packages to install additional packages'
                print 'Remove to existing version to re-install the Unity version'
                print 'Separate --download and --install calls will re-install the Unity version'
                print ''
                continue
            
            if operation == 'download' or not operation:
                download(version, path, config, selected)
            
            if operation == 'install' or not operation:
                install(version, path, selected)
                
                if not args.keep and not operation:
                    clean_up(path)
    
    if operation == 'list':
        print 'Packages with a * are installed by default if no packages are selected'
        print 'Select packages to install using "--package NAME"'