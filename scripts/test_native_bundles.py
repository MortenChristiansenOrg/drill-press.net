"""Contract checks for the fail-closed NativeAOT warning gate."""

import unittest
from unittest.mock import patch

from native_bundles import display_path, validate_publish_warnings


class PublishWarningTests(unittest.TestCase):
    def test_accepts_documented_member_with_unix_diagnostic_prefix(self):
        output = "/_/CommonCompiler.cs(175): warning IL3000: Microsoft.CodeAnalysis.CommonCompiler.GetAssemblyLocation(Type): explanation [bundle.csproj]"

        validate_publish_warnings(output)

    def test_accepts_documented_member_with_windows_diagnostic_prefix(self):
        output = r"C:\src\CommonCompiler.cs(175): warning IL3000: Microsoft.CodeAnalysis.CommonCompiler.GetAssemblyLocation(Type): explanation [C:\bundle.csproj]"

        validate_publish_warnings(output)

    def test_rejects_same_code_from_another_member(self):
        output = "ILC : warning IL3000: Other.Type.Location(): explanation"

        with self.assertRaisesRegex(RuntimeError, "Unexplained publication warning"):
            validate_publish_warnings(output)

    def test_rejects_new_code_from_a_known_member(self):
        output = "ILC : warning IL2026: Microsoft.CodeAnalysis.CommonCompiler.GetAssemblyLocation(Type): explanation"

        with self.assertRaisesRegex(RuntimeError, "Unexplained publication warning"):
            validate_publish_warnings(output)

    def test_rejects_non_ilc_warnings(self):
        output = "bundle.csproj: warning NU1605: Detected package downgrade"

        with self.assertRaisesRegex(RuntimeError, "Unexplained publication warning"):
            validate_publish_warnings(output)


class DisplayPathTests(unittest.TestCase):
    def test_keeps_an_absolute_path_when_windows_drives_differ(self):
        source = r"C:\temp\Probe.cs"

        with patch("native_bundles.os.path.relpath", side_effect=ValueError("different drives")):
            result = display_path(source)

        self.assertEqual("C:/temp/Probe.cs", result)

    def test_normalizes_relative_windows_separators(self):
        source = r"D:\a\temp\Probe.cs"

        with patch("native_bundles.os.path.relpath", return_value=r"..\temp\Probe.cs"):
            result = display_path(source)

        self.assertEqual("../temp/Probe.cs", result)


if __name__ == "__main__":
    unittest.main()
