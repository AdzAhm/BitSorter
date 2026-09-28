"""Stage only some hunks of one file.

    stage_hunks.py <file> <exclude-phrase>...

Stages every hunk of <file>'s working-tree diff except those containing any of the phrases, so a
file holding two unrelated changes -- usually CLAUDE.md -- can go into two commits. Run from
anywhere inside the repository. Check the result with `git diff --cached <file>`.
"""
import os
import subprocess
import sys

top = subprocess.run(['git', 'rev-parse', '--show-toplevel'], capture_output=True, text=True).stdout.strip()
os.chdir(top)

path = sys.argv[1]
excludes = sys.argv[2:]

diff = subprocess.run(['git', 'diff', '-U3', '--', path], capture_output=True).stdout.decode('utf-8')
lines = diff.splitlines(keepends=True)

header = []
hunks = []
current = None
for line in lines:
    if line.startswith('@@'):
        current = [line]
        hunks.append(current)
    elif current is None:
        header.append(line)
    else:
        current.append(line)

kept = [h for h in hunks if not any(x in ''.join(h) for x in excludes)]
print(f'{len(hunks)} hunks, staging {len(kept)}')

if kept:
    patch = ''.join(header) + ''.join(''.join(h) for h in kept)
    r = subprocess.run(['git', 'apply', '--cached', '--recount', '-'],
                       input=patch.encode('utf-8'), capture_output=True)
    if r.returncode != 0:
        print(r.stderr.decode('utf-8'))
        sys.exit(r.returncode)
