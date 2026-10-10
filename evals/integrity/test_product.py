"""The staged CLI loads its native dependencies within the server boundary."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from TrialBoundary import boundary


@unittest.skipUnless(sys.platform == 'linux' and shutil.which('bwrap') and os.environ.get('MOTIF_PANGLOSS_EXE'),
                     'Product boundary requires Linux, bubblewrap, and the pinned PanGloss executable')
class ProductTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        super().setUpClass()
        with tempfile.TemporaryDirectory() as directory:
            session = Path(directory) / 'session'
            (session / 'output').mkdir(parents=True)
            probe = subprocess.run(boundary({'session': str(session), 'mounts': []}, ['/usr/bin/true']),
                                   stdin=subprocess.DEVNULL, capture_output=True, timeout=15)
        if b'NETLINK_ROUTE socket: Operation not permitted' in probe.stderr:
            raise unittest.SkipTest('The sandbox does not allow bubblewrap to create its private network namespace')
        if probe.returncode:
            raise RuntimeError(probe.stderr.decode(errors='replace'))

    def test_staged_baseline_capture_loads_native_assets(self):
        repo = Path(__file__).resolve().parents[2]
        build = repo / 'bin' / os.environ.get('MOTIF_TEST_CONFIGURATION', 'Debug')
        grammars = Path(os.environ['MOTIF_TEST_GRAMMARS'])
        sets = grammars / 'evals/sets' if (grammars / 'evals/sets').exists() else grammars / 'sets'
        task = json.loads((sets / 'eval-t1-vexu/tasks/t1-safety-guess-and-apply/task.yaml').read_text())
        parser = Path(os.environ['MOTIF_PANGLOSS_EXE'])

        def quote(path):
            return "'" + str(path).replace("'", "''") + "'"

        storage = repo / 'evals/tools/ABHarness.psm1'
        selected = subprocess.run(['pwsh', '-NoProfile', '-Command', f'Import-Module {quote(storage)}; Get-ABTrialBase {quote(repo)}'],
                                  stdin=subprocess.DEVNULL, capture_output=True, timeout=30)
        self.assertEqual(0, selected.returncode, selected.stderr.decode())
        base = Path(selected.stdout.decode().strip())
        with tempfile.TemporaryDirectory(dir=base) as directory:
            root = Path(directory)
            session = root / 'session'
            (session / 'output').mkdir(parents=True)
            env = dict(os.environ)
            env.update({'MOTIF_WORKER_ROOT': str(root / 'prepare/work'),
                        'MOTIF_RUNNER_NAMESPACE': 'prepare-' + root.name,
                        'MOTIF_WRITING_SYSTEM_REPOSITORY_PATH': str(root / 'prepare/writing-systems'),
                        'MOTIF_TEST_SLDR_CACHE_PATH': str(root / 'prepare/sldr'), 'MOTIF_TEST_SLDR_OFFLINE': '1'})
            prepared = subprocess.run([str(build / 'SIL.Motif.EvalSets'), 'build-project', '--set', str(sets / task['set']),
                                       '--start', task['start'], '--out', str(root / 'seed')],
                                      env=env, stdin=subprocess.DEVNULL, capture_output=True, timeout=120)
            self.assertEqual(0, prepared.returncode, prepared.stderr.decode())
            project = Path(json.loads(prepared.stdout)['projectPath'])
            shutil.copytree(project.parent, session / 'project')
            module = repo / 'evals/tools/ProductStaging.psm1'
            runtime_source = Path(os.environ.get('DOTNET_ROOT', str(Path.home() / '.dotnet')))
            stage = subprocess.run(['pwsh', '-NoProfile', '-Command',
                                    f'Import-Module {quote(module)}; Get-ABRuntimeLayers {quote(build)} {quote(parser)} {quote(runtime_source)} {quote(base / "layers")} | ConvertTo-Json -Compress'],
                                   stdin=subprocess.DEVNULL, capture_output=True, timeout=180)
            self.assertEqual(0, stage.returncode, stage.stderr.decode())
            layers = json.loads(stage.stdout)
            product, runtime = Path(layers['product']), Path(layers['runtime'])
            native_assets = set()
            for name in ('motif', 'SIL.Motif.Worker'):
                dependencies = json.loads((build / (name + '.deps.json')).read_text())
                for packages in dependencies['targets'].values():
                    for package in packages.values():
                        for asset, metadata in package.get('runtimeTargets', {}).items():
                            if metadata['assetType'] == 'native' and (build / asset).is_file():
                                native_assets.add(asset)
            self.assertTrue(native_assets)
            for asset in native_assets:
                self.assertTrue((product / asset).is_file(), 'Missing declared native asset: ' + asset)
            config = {'session': str(session), 'mounts': [[str(product), '/opt/product'], [str(runtime), '/opt/runtime']]}
            result = subprocess.run(boundary(config, ['/opt/product/motif', 'baseline', 'capture',
                                                     '/session/project/' + project.name, '--json']),
                                    stdin=subprocess.DEVNULL, capture_output=True, timeout=120)
            self.assertEqual(0, result.returncode, result.stderr.decode())
            response = json.loads(result.stdout)
            self.assertTrue(response['token'], response)
            self.assertTrue((session / Path(response['fwDataPath']).relative_to('/session')).is_file())
            self.assertTrue(any((session / 'project').glob('*.motif.db')))
            parser_check = subprocess.run(boundary(config, ['/opt/product/pangloss', '--version']),
                                          stdin=subprocess.DEVNULL, capture_output=True, timeout=30)
            self.assertEqual(0, parser_check.returncode, parser_check.stderr.decode())
            pin = json.loads((repo / 'pangloss-release.json').read_text())
            self.assertIn(pin['version'], parser_check.stdout.decode())
            readonly = subprocess.run(boundary(config, ['/usr/bin/python3', '-c',
                "import errno;\ntry: open('/opt/product/pangloss','wb'); raise RuntimeError('shared runtime is writable')\nexcept OSError as error: assert error.errno==errno.EROFS"]),
                stdin=subprocess.DEVNULL, capture_output=True, timeout=30)
            self.assertEqual(0, readonly.returncode, readonly.stderr.decode())
