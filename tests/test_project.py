import pathlib
import subprocess
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]

class ProjectContract(unittest.TestCase):
    def test_standalone_project(self):
        project = ROOT / 'src/BigBlueFish.csproj'
        self.assertTrue(project.is_file())
        self.assertNotIn('ProjectReference', project.read_text())
        self.assertFalse((ROOT / 'code').exists())

    def test_make_interface(self):
        for target in ('configure', 'build', 'run', 'test', 'pack', 'install', 'uninstall', 'clean'):
            result = subprocess.run(['make', '-n', target], cwd=ROOT, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)

    def test_assets(self):
        import json
        assets = ROOT / 'assets/fish'
        rules = json.loads((assets / 'animations.json').read_text())
        self.assertEqual(len(rules), 106)
        for rule in rules:
            self.assertTrue((assets / rule['File']).is_file(), rule['File'])
            self.assertTrue((assets / 'compat' / rule['File']).is_file(), rule['File'])

if __name__ == '__main__':
    unittest.main()
