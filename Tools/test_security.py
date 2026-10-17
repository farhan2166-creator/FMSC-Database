"""Disposable, never valid credentials; no fixture is committed to this project."""
import argparse
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from check_security import ROOT, forbidden_file

SCANNER = "gitleaks"
FIXTURE_LINE = "const string " + "password" + '="' + "Fixture password 12345" + '";'


class SecurityChecks(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="fmsc-security-test-")
        self.repo = Path(self.directory.name)
        self.git("init", "-q")
        self.git("config", "user.name", "Security fixture")
        self.git("config", "user.email", "fixture@example.invalid")
        self.git("commit", "--allow-empty", "-qm", "Empty synthetic fixture")

    def tearDown(self):
        self.directory.cleanup()

    def git(self, *args):
        subprocess.run(["git", "-C", str(self.repo), *args], check=True, capture_output=True)

    def write(self, name, text):
        path = self.repo / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        self.git("add", "--", name)

    def check_repo(self):
        return subprocess.run([sys.executable, str(ROOT / "Tools/check_security.py"), "--repo", str(self.repo), "--gitleaks", SCANNER], capture_output=True, text=True)

    def test_forbidden_names_and_legitimate_files(self):
        for name in ("Database/STUDENT.MDF", "school.ldf", "backup.BAK", "school.ndf", ".env", ".env.production",
                     "secrets.json", "credentials.json", "appsettings.Production.json", "profile.pubxml.user", "key.pfx", ".aws/credentials"):
            self.assertTrue(forbidden_file(name), name)
        for name in ("FMSC.dacpac", "Properties/PublishProfiles/PortableRelease.pubxml", "Core/PermissionCatalog.json",
                     "Services/Communication/Schema.sql", "DatabaseHelper.cs", ".env.example", ".env.sample", ".env.template"):
            self.assertFalse(forbidden_file(name), name)

    def test_exact_existing_fixture_is_allowed(self):
        self.write("Tests/AdministrationSqlChecks/Program.cs", FIXTURE_LINE)
        self.assertEqual(0, self.check_repo().returncode)

    def test_ui_password_placeholder_is_not_a_secret(self):
        self.write("ui.cs", 'password.PlaceholderText = isNewUser\n ? "Enter password"\n : "Leave blank to keep current password";')
        self.assertEqual(0, self.check_repo().returncode)

    def test_tokens_passwords_keys_and_no_secret_output(self):
        dummy_token = "ghp_" + "A1b2C3d4" * 5
        dummy_value = "not-real-" + "P8ssword-72"
        encoded_body = "QUJDREVGR0hJSktM" + "TU5PUFFSU1RVVldYWVo="
        private_key = "-----BEGIN " + "PRIVATE KEY-----\n" + encoded_body * 4 + "\n-----END " + "PRIVATE KEY-----\n"
        self.write("Tests/AdministrationSqlChecks/Program.cs", FIXTURE_LINE + "\n" + "token=" + dummy_token)
        self.write("dummy.txt", "password=" + '"' + dummy_value + '"\n' + private_key)
        result = self.check_repo()
        self.assertEqual(1, result.returncode)
        for value in (dummy_token, dummy_value, encoded_body):
            self.assertTrue(value not in result.stdout + result.stderr, "A fixture value appeared in scanner output")
        for rule in ("github-pat", "fmsc-literal-password", "private-key"):
            self.assertIn(rule, result.stdout)

    def test_fixture_value_outside_test_file_is_not_exempt(self):
        self.write("production.cs", FIXTURE_LINE)
        self.assertEqual(1, self.check_repo().returncode)

    def test_connection_password_in_example_is_detected(self):
        self.write(".env.example", 'connection="Server=localhost;Database=dummy;Password=' + "not-real-72" + '"')
        result = self.check_repo()
        self.assertEqual(1, result.returncode)
        self.assertIn("fmsc-connection-password", result.stdout)

    def test_deleted_secret_is_detected_in_history(self):
        self.write("deleted.txt", "token=" + "ghp_" + "A1b2C3d4" * 5)
        self.git("commit", "-qm", "Synthetic token")
        self.git("rm", "--", "deleted.txt")
        self.git("commit", "-qm", "Remove synthetic token")
        self.assertEqual(1, self.check_repo().returncode)

    def test_deleted_database_file_is_detected_in_history(self):
        self.write("deleted.BAK", "Harmless filename fixture")
        self.git("commit", "-qm", "Synthetic database filename")
        self.git("rm", "--", "deleted.BAK")
        self.git("commit", "-qm", "Remove fixture")
        result = self.check_repo()
        self.assertEqual(1, result.returncode)
        self.assertIn("Sensitive files", result.stdout)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--gitleaks", default="gitleaks")
    args = parser.parse_args()
    SCANNER = args.gitleaks
    unittest.main(argv=[sys.argv[0]])
