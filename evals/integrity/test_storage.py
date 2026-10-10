"""Disk root selection, shared layer publication, and owned staging cleanup."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]


def quote(path):
    return "'" + str(path).replace("'", "''") + "'"


def powershell(script, env=None):
    result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], env=env,
                            stdin=subprocess.DEVNULL, capture_output=True, timeout=60)
    if result.returncode:
        raise AssertionError(result.stderr.decode())
    return result.stdout.decode().strip()


class StorageTests(unittest.TestCase):
    @unittest.skipUnless(os.name == 'posix', 'Link cleanup regression uses Unix links')
    def test_cleanup_unlinks_links_without_touching_foreign_targets(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            protected = root / 'protected'
            protected.mkdir()
            evidence = protected / 'answer'
            evidence.write_text('protected')
            module = REPO / 'evals/tools/ABHarness.psm1'
            trial = Path(powershell(f'Import-Module {quote(module)}; New-ABTrialDirectory {quote(root)} {quote(root)}'))
            (trial / 'file-link').symlink_to(evidence)
            (trial / 'directory-link').symlink_to(protected, target_is_directory=True)
            (trial / 'broken-link').symlink_to(root / 'absent')
            alias = root / 'alias'
            alias.symlink_to(trial, target_is_directory=True)
            script = f'''Import-Module {quote(module)}
$denied=$false
try {{ Remove-ABTrialDirectory {quote(alias)} }} catch {{ $denied=$true }}
if (-not $denied) {{ throw 'Linked root accepted' }}
Remove-ABTrialDirectory {quote(trial)}
'''
            powershell(script)
            self.assertFalse(trial.exists())
            self.assertEqual('protected', evidence.read_text())

    def test_root_precedence_keep_and_owned_cleanup(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            script = f'''Import-Module {quote(REPO / 'evals/tools/ABHarness.psm1')}
$env:MOTIF_TRIAL_ROOT=''; $env:TMPDIR=''
if ((Get-ABTrialBase {quote(root)}) -ne {quote(root / 'bin/.cache/ab-trials')}) {{ throw 'Default is not checkout disk cache' }}
$env:TMPDIR={quote(root / 'temporary')}
if ((Get-ABTrialBase {quote(root)}) -ne $env:TMPDIR) {{ throw 'TMPDIR ignored' }}
$env:MOTIF_TRIAL_ROOT={quote(root / 'disk')}
if ((Get-ABTrialBase {quote(root)}) -ne $env:MOTIF_TRIAL_ROOT) {{ throw 'Explicit root ignored' }}
$trial=New-ABTrialDirectory {quote(root)}
Remove-ABTrialDirectory $trial -Keep
if (-not (Test-Path $trial)) {{ throw 'Kept staging deleted' }}
Remove-ABTrialDirectory $trial
if (Test-Path $trial) {{ throw 'Completed staging retained' }}
$denied=$false
try {{ Remove-ABTrialDirectory $env:MOTIF_TRIAL_ROOT }} catch {{ $denied=$true }}
if (-not $denied) {{ throw 'Unowned root deleted' }}
'''
            powershell(script)

    def test_concurrent_layers_reuse_bytes_and_content_changes_invalidate(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            build, runtime, layers = root / 'build', root / 'runtime', root / 'layers'
            for path in (build / 'motif', build / 'runtimes/linux-x64/native/native.so',
                         build / 'IcuData/data/normalization', runtime / 'dotnet',
                         runtime / 'host/fxr/runtime.so', runtime / 'shared/framework/runtime.so', root / 'parser'):
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('original')
            script = f'''Import-Module {quote(REPO / 'evals/tools/ProductStaging.psm1')}
Get-ABRuntimeLayers {quote(build)} {quote(root / 'parser')} {quote(runtime)} {quote(layers)} | ConvertTo-Json -Compress
'''
            processes = [subprocess.Popen(['pwsh', '-NoProfile', '-Command', script], stdin=subprocess.DEVNULL,
                                          stdout=subprocess.PIPE, stderr=subprocess.PIPE) for _ in range(2)]
            published = []
            for process in processes:
                output, error = process.communicate(timeout=60)
                self.assertEqual(0, process.returncode, error.decode())
                published.append(json.loads(output))
            self.assertEqual(published[0], published[1])
            first = published[0]
            self.assertEqual('original', (Path(first['runtime']) / 'dotnet').read_text())
            self.assertEqual(2, len([path for path in layers.iterdir() if path.is_dir()]))
            asset = build / 'runtimes/linux-x64/native/native.so'
            timestamp = asset.stat()
            asset.write_text('modified')
            os.utime(asset, ns=(timestamp.st_atime_ns, timestamp.st_mtime_ns))
            changed = json.loads(powershell(script))
            self.assertNotEqual(first['product'], changed['product'])
            self.assertEqual(first['runtime'], changed['runtime'])
            self.assertEqual('original', (Path(first['product']) / 'runtimes/linux-x64/native/native.so').read_text())
            self.assertEqual('modified', (Path(changed['product']) / 'runtimes/linux-x64/native/native.so').read_text())
            self.assertFalse(list(layers.glob('*.partial')))

    def test_runner_failure_cleans_staging_unless_kept(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            script = f'''Import-Module {quote(REPO / 'evals/tools/ABHarness.psm1')}
$env:MOTIF_TRIAL_ROOT={quote(root)}
foreach ($mode in @('delete', 'manifest', 'switch', 'double')) {{
 $keep=$mode -ne 'delete'
 $trial=New-ABTrialDirectory {quote(REPO)}
 $manifest=@{{trialRoot=$trial; repoRoot={quote(REPO)}; keep=($mode -eq 'manifest'); task=@{{}}; taskPath=''; arm=@{{server=@{{profilePath='missing-profile.json'}}}}}}
 $file=Join-Path $trial 'manifest.json'; Write-ABJson $file $manifest
 $arguments=@('-NoProfile','-File',{quote(REPO / 'evals/tools/Invoke-ABTrial.ps1')},'-RunManifest',$file)
 if ($mode -eq 'switch') {{ $arguments+='-Keep' }}
 if ($mode -eq 'double') {{ $arguments+='--keep' }}
 & pwsh @arguments 2>$null
 if ($LASTEXITCODE -eq 0) {{ throw 'Expected setup failure' }}
 if ((Test-Path $trial) -ne $keep) {{ throw 'Runner cleanup ignored keep' }}
 Remove-ABTrialDirectory $trial
}}
'''
            powershell(script)
