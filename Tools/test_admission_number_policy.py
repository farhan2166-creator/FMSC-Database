"""Run the production numbering policy on synthetic records, without SQL or WinForms.

Usage: python Tools/test_admission_number_policy.py --dotnet dotnet --sqlclient path/to/Microsoft.Data.SqlClient.dll
The SqlClient reference is needed to compile unused query methods; no connection is opened.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
HARNESS = r'''
global using System.Data;
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;

internal static class Program
{
    static int checks;
    static void Check(bool result, string label)
    {
        if (!result) throw new Exception(label);
        checks++;
    }
    static DataTable Students(params (DateTime Date, string Number)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("AdmissionDate", typeof(DateTime));
        table.Columns.Add("AdmissionNumber", typeof(string));
        foreach (var row in rows) table.Rows.Add(row.Date, row.Number);
        return table;
    }
    static void Main()
    {
        DateTime start = new(2020, 4, 1), end = new(2021, 3, 31), date = new(2020, 4, 15);
        var students = Students((new(2020, 4, 10), "101"), (new(2020, 4, 20), "103"));
        var plan = AdmissionNumberPolicy.Calculate(students, date, start, end);
        Check(plan.Suggested == "101a", "Chronological suggestion changed");
        Check(plan.Validate("100") is null, "Unique override below date-order bound was blocked");
        Check(plan.Validate("104") is null, "Unique override above date-order bound was blocked");
        Check(plan.Review("100") is not null && plan.Review("104") is not null, "Optional chronology review disappeared");
        Check(plan.Validate("000101") is not null, "Normalized duplicate was accepted");
        Check(plan.Validate("0") is not null && plan.Validate("12-3") is not null, "Malformed number was accepted");
        Check(plan.Validate("102") is null, "Ordinary in-order number was blocked");
        var conflict = AdmissionNumberPolicy.Calculate(
            Students((new(2020, 4, 10), "1027"), (new(2020, 4, 20), "32")), date, start, end);
        Check(conflict.Error is null && conflict.Suggested is null, "Conflicting old chronology blocks the plan or suggests an invalid number");
        Check(conflict.Validate("1030b") is null, "Manual number was blocked by conflicting old chronology");
        Check(conflict.Validate("32") is not null && conflict.Review("1030b") is not null, "Conflict discarded duplicate protection or optional review");
        Check(AdmissionNumberPolicy.Calculate(students, start.AddDays(-1), start, end).Validate("104") is not null,
            "Out-of-year admission was accepted");
        DateTime future = DateTime.Today.AddDays(1);
        Check(AdmissionNumberPolicy.Calculate(students, future, future.AddDays(-1), future.AddDays(1)).Validate("104") is not null,
            "Future admission was accepted");
        var historical = AdmissionNumberPolicy.Calculate(students, date, start, end, true);
        Check(historical.Lower is null && historical.Upper is null && historical.Validate("104") is null,
            "Historical register exception changed");
        Check(historical.Validate("101") is not null, "Historical mode allowed a duplicate");
        var suffix = AdmissionNumberPolicy.Calculate(Students((date, "00101A")), date, start, end);
        Check(suffix.Validate("101a") is not null, "Case-insensitive normalized duplicate was accepted");
        Console.WriteLine($"Passed {checks} production numbering-policy checks; no SQL connection opened.");
    }
}

namespace FMSC_SMS
{
    internal static class DatabaseHelper
    {
        public static SqlConnection GetConnection() => throw new Exception("SQL access is forbidden in this harness");
        public static void OpenConnection(SqlConnection connection) => throw new Exception("SQL access is forbidden in this harness");
        public static SqlParameter Int(string name, int value) => new(name, SqlDbType.Int) { Value = value };
    }
    internal static class CurrentUser { public static int UserId => 1; }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--sqlclient", required=True, type=Path)
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="fmsc-numbering-") as folder:
        directory = Path(folder)
        project = directory / "NumberingChecks.csproj"
        project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup>
    <Compile Include="{escape(str(ROOT / 'Core/AdmissionNumberPolicy.cs'))}" Link="AdmissionNumberPolicy.cs" />
    <Compile Include="{escape(str(ROOT / 'Core/AdmissionNumbers.cs'))}" Link="AdmissionNumbers.cs" />
    <Reference Include="Microsoft.Data.SqlClient"><HintPath>{escape(str(args.sqlclient.resolve()))}</HintPath></Reference>
  </ItemGroup>
</Project>''')
        (directory / "Program.cs").write_text(HARNESS)
        # No NuGet packages are needed by this harness. Restore only SDK framework assets.
        config = directory / "NuGet.Config"
        config.write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        subprocess.run([args.dotnet, "restore", str(project), "--configfile", str(config)], check=True)
        subprocess.run([args.dotnet, "run", "--project", str(project), "--no-restore"], check=True)


if __name__ == "__main__":
    main()
