#!/usr/bin/python
# -*- coding: utf-8 -*-

import argparse
import urllib
import urllib2
import re
import json
import datetime
import dateutil.parser
import io
import os
import sys
import ConfigParser
import math
import hashlib
import subprocess
import pipes
import getpass

# ---- CONFIGURATION ----

VERSION = '0.0.1'

UNITY_DOWNLOADS = 'http://unity3d.com/get-unity/download/archive'
UNITY_PATCHES = 'http://unity3d.com/unity/qa/patch-releases'
UNITY_DOWNLOADS_RE = '"(https?:\/\/[\w\/.-]+\/[0-9a-f]{12}\/)MacEditorInstaller\/[\w\/.-]+(\d+\.\d+\.\d+\w\d+)[\w\/.-]+"'
UNITY_INI_NAME = 'unity-%s-osx.ini'

CACHE_FILE = 'unity_versions.json'
CACHE_LIFETIME = 60*60*24

VERSION_RE = '^(\d+)(?:\.(\d+)(?:\.(\d+))?)?(?:(\w)(?:(\d+))?)?$'
RELEASE_LETTERS = { 'release': 'f', 'patch': 'p' }
RELEASE_LETTER_STRENGTH = { 'f': 1, 'p': 2 }

DOWNLOAD_PATH = '~/Downloads/Unity Install Manager/%s'

# ---- ARGUMENTS ----

parser = argparse.ArgumentParser(description='Unity Installation Manager ' + VERSION)
parser.add_argument('--version', action='version', version='%(prog)s ' + VERSION)

parser.add_argument('versions', 
    metavar='VERSION', type=str, nargs='*',
    help='unity version to install (only >= 5.0.0)')

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
    help='add package to download or install, default is to install all available')
parser.add_argument('--no-move', 
    action='store_true',
    help='don\'t manage existing unity installations by moving them')
parser.add_argument('-k', '--keep', 
    action='store_true',
    help='don\'t remove installer files after installation')

parser.add_argument('-u', '--update', 
    action='store_true',
    help='force updating of cached version information')
parser.add_argument('--list-versions', 
    choices=['release', 'patch', 'all'],
    help='list the cached unity versions')

args = parser.parse_args()

# ---- GENERAL ----

def error(message):
    print 'ERROR: ' + message
    sys.exit(1)

# ---- VERSIONS CACHE ----

def update_version_cache(unity_versions):
    print 'Updating Unity versions list...'
    
    print 'Loading Unity releases...'
    count = load_and_parse(UNITY_DOWNLOADS, UNITY_DOWNLOADS_RE, unity_versions)
    if count > 0: print 'Found %i Unity releases.' % count
    
    print 'Loading Unity patch releases...'
    count = load_and_parse(UNITY_PATCHES, UNITY_DOWNLOADS_RE, unity_versions)
    if count > 0: print 'Found %i Unity patch releases.' % count
    
    with open(os.path.join(script_dir, CACHE_FILE), 'w') as file:
        data = json.dumps({'lastupdate': datetime.datetime.utcnow().isoformat(), 'versions': unity_versions})
        file.write(data)

def load_and_parse(url, pattern, unity_versions):
    try:
        response = urllib2.urlopen(url)
    except Exception as e:
        error('Could not load URL "%s": %s' % url, e.reason)
    
    result = re.findall(pattern, response.read())
    for match in result:
        unity_versions[match[1]] = match[0]
    return len(result)

def read_versions_cache():
    path = os.path.join(script_dir, CACHE_FILE)
    if not os.path.isfile(path):
        return None
    
    with open(path, 'r') as file:
        data = file.read()
        obj = json.loads(data)
        
        lastupdate = dateutil.parser.parse(obj['lastupdate'])
        if (datetime.datetime.utcnow() - lastupdate).total_seconds() > CACHE_LIFETIME:
            return None
        
        return obj['versions']

def list_versions(type):
    letter = None
    if type:
        letter = RELEASE_LETTERS[type]
    
    print 'Available Unity versions:'
    for version in sorted_versions:
        if letter and not letter in version:
            continue
        print '- %s' % version

# ---- VERSION HANDLING ----

def parse_version(version):
    match = re.match(VERSION_RE, version)
    if not match:
        error('Version %s does not conform to Unity version format 0.0.0x0' % version)
    
    parts = list(match.groups())
    
    for i in range(len(parts)):
        if not parts[i] or i == 3: continue
        parts[i] = int(parts[i])
    
    if parts[3]:
        if not parts[3] in RELEASE_LETTER_STRENGTH:
            error('Unknown release letter "%s"' % parts[3])
        parts[3] = RELEASE_LETTER_STRENGTH[parts[3]]
    
    return parts

def compare_versions(one, two):
    return cmp(parse_version(one), parse_version(two))

def match_version(one, two):
    for i in range(5):
        if one[i] and two[i] and one[i] != two[i]:
            return False
    return True

def select_version(version):
    one = parse_version(version)
    
    for i in reversed(range(len(sorted_versions))):
        two = parse_version(sorted_versions[i])
        if match_version(one, two):
            if version != sorted_versions[i]:
                print 'Selected version %s for input version %s' % (sorted_versions[i], version)
            return sorted_versions[i]
    
    error('Version %s is now a known Unity version' % version)

# ---- INSTALLATION ----

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

def progress(blocknr, blocksize, size):
    current = min(1.0, (blocknr * blocksize) / float(size))
    
    sys.stdout.write("\033[F")
    sys.stdout.write("\033[K")
    
    sys.stdout.write('[')
    sys.stdout.write('|' * int(math.floor(current * 60)))
    sys.stdout.write(' ' * int(math.ceil((1 - current) * 60)))
    sys.stdout.write('] {0:.2f}%\n'.format((100.0 * current)))

def hashfile(path, blocksize=65536):
    with open(path, 'rb') as file:
        hasher = hashlib.md5()
        buf = file.read(blocksize)
        while len(buf) > 0:
            hasher.update(buf)
            buf = file.read(blocksize)
        return hasher.hexdigest()

def load_ini(version, path):
    if not version in unity_versions:
        error('Version %s is now a known Unity version' % version)
    
    ini_name = UNITY_INI_NAME % version
    ini_path = os.path.join(path, ini_name)
    
    if not os.path.isfile(ini_path) or args.update:
        url = unity_versions[version] + ini_name
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
        selected = available
    else:
        lower_to_upper = {}
        for pkg in available:
            lower_to_upper[pkg.lower()] = pkg
        
        selected = []
        for select in packages:
            if select.lower() in lower_to_upper:
                selected.append(lower_to_upper[select.lower()])
            else:
                print 'WARNING: Unity version %s has no package "%s"' % (version, select)
    
    if 'Unity' in selected:
        selected.remove('Unity')
        selected.insert(0, 'Unity')
    
    return selected

def download(version, path, config, selected):
    print 'Downloading Unity %s...' % version
    
    for pkg in selected:
        fileurl = unity_versions[version] + config.get(pkg, 'url')
        filename = os.path.basename(fileurl)
        output = os.path.join(path, filename)
        
        print 'Downloading %s (%s)...' % (filename, convertSize(config.getint(pkg, 'size')))
        download_url(fileurl, output)
        
        if not config.has_option(pkg, 'md5'):
            print 'WARNING: Cannot verify file "%s": No md5 hash found.' % filename
        else:
            digest = hashfile(output)
            if not digest == config.get(pkg, 'md5'):
                error('Downloaded file "%s" is corrupt, hash does not match.' % filename)

def find_unity_installs():
    installs = {}
    
    app_dir = os.path.join(args.volume, 'Applications')
    if not os.path.isdir(app_dir):
        error('Applications directory on target volume "%s" not found' % args.volume)
    
    install_paths = [x for x in os.listdir(app_dir) if x.startswith('Unity')]
    for install_path in install_paths:
        plist_path = os.path.join(app_dir, install_path, 'Unity.app', 'Contents', 'Info.plist')
        if not os.path.isfile(plist_path):
            print "WARNING: No Info.plist found at '%s'" % plist_path
            continue
        
        installed_version = subprocess.check_output(['defaults', 'read', plist_path, 'CFBundleVersion']).strip()
        
        installs[installed_version] = install_path
    
    print 'Found %d existing Unity installations' % len(installs)
    
    return installs

def install(version, path, selected):
    print 'Installing Unity %s...' % version
    
    if not version in installs and not 'Unity' in selected:
            error('Installing only components but no matching Unity %s installation found' % version)
    
    install_path = os.path.join(args.volume, 'Applications', 'Unity')
    moved_unity_to = None
    if version in installs and installs[version] == 'Unity':
        pass
    elif os.path.isdir(install_path):
        lookup = [vers for vers,name in installs.iteritems() if name == 'Unity']
        if len(lookup) != 1:
            error('Directory "%s" not recognized as Unity installation.' % install_path)
        
        moved_unity_to = os.path.join(args.volume, 'Applications', 'Unity %s' % lookup[0])
        os.rename(install_path, moved_unity_to)
    
    moved_unity_from = None
    if version in installs and installs[version] != 'Unity':
        moved_unity_from = os.path.join(args.volume, 'Applications', installs[version])
        os.rename(moved_unity_from, install_path)
    
    for pkg in selected:
        filename = os.path.basename(config.get(pkg, 'url'))
        package = os.path.join(path, filename)
        
        print 'Installing %s...' % filename
        
        command = 'echo "%s" | /usr/bin/sudo -S /usr/sbin/installer -pkg %s -target %s -verbose' % (pwd, pipes.quote(package), pipes.quote(args.volume))
        try:
            subprocess.check_output(command, shell=True, stderr=subprocess.STDOUT)
        except subprocess.CalledProcessError as e:
            error('Installation of package "%s" failed: %s' % (filename, e.output))
    
    if moved_unity_from:
        os.rename(install_path, moved_unity_from)
    
    if moved_unity_to:
        os.rename(moved_unity_to, install_path)

# ---- MAIN ----

script_dir = os.path.dirname(os.path.abspath(__file__))
operation = args.operation
packages = [x.lower() for x in args.package] if args.package else []

# Version cache
unity_versions = read_versions_cache() or {}
if args.update or not unity_versions:
    update_version_cache(unity_versions)

sorted_versions = sorted(unity_versions.keys(), compare_versions)

# Start root shell if necessary
if not operation or operation == 'install':
    # Get root password for installation
    pwd = getpass.getpass('Root password:')
    command = 'sudo -k && echo "%s" | /usr/bin/sudo -S /usr/bin/whoami' % pwd
    result = subprocess.call(command, shell=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result != 0:
        error('Root password invalid (%d)' % result)
    
    # Find existing installations
    installs = find_unity_installs()

# Main Operation
if args.list_versions or len(args.versions) == 0:
    list_versions(args.list_versions)

else:
    versions = set(map(select_version, args.versions))
    
    for version in versions:
        path = os.path.expanduser(DOWNLOAD_PATH % version)
        if not os.path.isdir(path):
            os.makedirs(path)
        
        config = load_ini(version, path)
        
        if operation == 'list':
            print 'Available packages for Unity version %s:' % version
            for pkg in config.sections():
                print '- %s (%s)' % (pkg, convertSize(config.getint(pkg, 'size')))
            continue
        
        selected = select_packages(config, packages)
        
        if operation == 'download' or not operation:
            download(version, path, config, selected)
        
        if operation == 'install' or not operation:
            install(version, path, selected)