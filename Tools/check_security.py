"""Fail closed before publication: inspect available Git history and run Gitleaks.

Reports paths and rule failures only; never print matched credentials.
"""
import argparse
import io
from pathlib import Path
import re
import subprocess
import shutil
import tempfile
import zipfile
import xml.etree.ElementTree as ET

FORBIDDEN = {'.mdf', '.ldf', '.ndf', '.bak', '.bacpac', '.pfx', '.p12', '.key'}


def forbidden_path(name):
    path = Path(name)
    base = path.name.lower()
    return (path.suffix.lower() in FORBIDDEN
            or base in {'secrets.json', 'credentials.json', 'appsettings.production.json'}
            or (base.startswith('.env') and base not in {'.env.example', '.env.sample', '.env.template'})
            or (base.endswith('.pem') and 'public' not in base))


def inspect_dacpac(data):
    """Schema packages must contain no exported rows, opaque payloads or scripts."""
    allowed = {'model.xml', 'DacMetadata.xml', '[Content_Types].xml', 'Origin.xml', '_rels/.rels'}
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        if set(archive.namelist()) - allowed:
            raise ValueError('DACPAC contains entries outside the schema-only allowlist')
        if '_rels/.rels' in archive.namelist():
            for relation in ET.fromstring(archive.read('_rels/.rels')):
                if relation.attrib.get('TargetMode', '').lower() == 'external':
                    raise ValueError('DACPAC contains an external relationship')
        origin = archive.read('Origin.xml').decode('utf-8-sig')
        if not re.search(r'<(?:\w+:)?ContainsExportedData>\s*false\s*</', origin, re.I):
            raise ValueError('DACPAC does not explicitly declare ContainsExportedData=false')
        return {name: archive.read(name) for name in archive.namelist()}


def git(*args):
    return subprocess.check_output(['git', *args])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--gitleaks', required=True)
    args = parser.parse_args()
    if git('rev-parse', '--is-shallow-repository').strip() != b'false':
        raise SystemExit('BLOCKED: full history is required (fetch-depth: 0).')
    failures = []
    # Enumerate every tree instead of parsing rename/diff display names.
    packages = {}
    for commit in git('rev-list', '--all').decode().splitlines():
        for entry in git('ls-tree', '-rz', commit).split(b'\0'):
            if not entry:
                continue
            meta, raw_path = entry.split(b'\t', 1)
            mode, kind, oid = meta.split()
            path = raw_path.decode('utf-8', errors='replace')
            if forbidden_path(path):
                failures.append('Forbidden historical path: ' + path)
            if kind == b'blob' and path.lower().endswith('.dacpac'):
                packages[oid.decode()] = path
    with tempfile.TemporaryDirectory(prefix='fmsc-schema-scan-') as temp:
        for oid, path in packages.items():
            try:
                content = inspect_dacpac(git('cat-file', 'blob', oid))
                folder = Path(temp) / 'packages' / oid
                folder.mkdir(parents=True)
                for name, data in content.items():
                    target = folder / name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(data)
            except (ValueError, KeyError, zipfile.BadZipFile, ET.ParseError):
                failures.append('DACPAC requires private review: ' + path)
        # Scan source files, including untracked candidates, without generated build
        # output/bytecode. Tracked ignored files are still included and history is
        # scanned independently. Never follow a source symlink outside the repository.
        source = Path(temp) / 'working-source'
        source.mkdir()
        for raw in git('ls-files', '--cached', '--others', '--exclude-standard', '-z').split(b'\0'):
            if not raw:
                continue
            original = Path(raw.decode('utf-8'))
            if original.is_symlink():
                failures.append('Source symlink requires review: ' + str(original))
                continue
            if not original.is_file():
                continue
            if forbidden_path(str(original)):
                failures.append('Forbidden working path: ' + str(original))
            target = source / original
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(original, target)
        # Redact findings; disallow inline/fingerprint suppression outside reviewed config.
        common = ['--config', str(Path('.gitleaks.toml').resolve()), '--redact', '--no-banner', '--ignore-gitleaks-allow', '--gitleaks-ignore-path', str(Path(temp) / 'no-ignores')]
        for command in (
            [args.gitleaks, 'git', '.', '--log-opts=--all', *common],
            [args.gitleaks, 'dir', '.', *common],
            [args.gitleaks, 'dir', str(Path(temp) / 'packages'), *common],
        ):
            result = subprocess.run(command, check=False,
                cwd=source if command[1:3] == ['dir', '.'] else None)
            if result.returncode:
                failures.append('Gitleaks scan failed or detected a secret (exit %s).' % result.returncode)
    for failure in sorted(set(failures)):
        print(failure)
    if failures:
        raise SystemExit(1)
    print('PASS: available history, working tree and unpacked historical DACPACs scanned.')
    print('Screenshots, remote-only refs, releases and existing Actions artifacts require separate review.')


if __name__ == '__main__':
    main()
