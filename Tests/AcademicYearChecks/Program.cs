using FMSC_SMS.Core;

int checks = 0;
foreach (var sample in new[] { ("2021-2022", 2021), ("2025-2026", 2025), ("1999-2000", 1999), (" 2022-2023 ", 2022), ("2099-2100", 2099) })
{
    if (!AcademicYearName.TryParse(sample.Item1, out int year) || year != sample.Item2)
        throw new InvalidOperationException("Valid range rejected: " + sample.Item1);
    checks++;
}
foreach (string? sample in new string?[] { null, "", " ", "2021-20224444", "20214444-2022", "2021", "2021-", "2021-202", "2021-2021", "2022-2021", "2021-2023", "21-22", "2021--2022", "2021/2022", "Year 2021-2022", "2021-2022x", "2021 -2022", "２０２１-２０２２", "0000-0001", "9999-0000" })
{
    if (AcademicYearName.TryParse(sample, out int year) || year != 0)
        throw new InvalidOperationException("Malformed range accepted: " + sample);
    checks++;
}
Console.WriteLine($"PASS: {checks} academic-year name checks, including the reported 2021-20224444 regression.");
