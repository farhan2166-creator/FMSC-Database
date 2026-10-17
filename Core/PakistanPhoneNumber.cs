namespace FMSC_SMS.Core;

internal static class PakistanPhoneNumber
{
    // PTA numbering plan published by ITU: https://www.ituob.org/issues/1233-en/
    private static readonly string[] AreaCodes = (
        "21 22 25 40 41 42 44 46 47 48 49 51 52 53 55 56 57 61 62 63 64 65 66 67 68 71 74 81 86 91 " +
        "232 233 235 238 242 243 244 297 298 453 454 457 459 542 543 544 546 547 604 606 608 " +
        "722 723 726 822 823 824 825 826 828 829 832 833 835 837 838 843 844 847 848 852 853 855 856 " +
        "922 923 924 925 926 927 928 932 937 938 939 942 943 944 945 946 963 965 966 969 992 995 996 997 998 " +
        "5811 5812 5813 5814 5815 5816 5817 5821 5822 5823 5824 5825 5826 5827 5828")
        .Split(' ');
    internal const string ValidationMessage = "Enter a full Pakistan phone number";
    internal static bool IsValidOptional(string? input)
    {
        string text = (input ?? string.Empty).Trim();
        if (text.Length == 0) return true;
        if (text.Any(c => !(c is >= '0' and <= '9' or '+' or '-' or '(' or ')' || char.IsWhiteSpace(c)))) return false;
        if (text.Count(c => c == '+') > 1 || (text.Contains('+') && !text.StartsWith('+'))) return false;
        int brackets = 0;
        foreach (char c in text)
        {
            if (c == '(' && ++brackets > 1) return false;
            if (c == ')' && --brackets < 0) return false;
        }
        if (brackets != 0) return false;
        string number = string.Concat(text.Where(c => c is >= '0' and <= '9'));
        if (text.StartsWith('+'))
        {
            if (!number.StartsWith("92", StringComparison.Ordinal)) return false;
            number = number[2..];
        }
        else if (number.StartsWith("0092", StringComparison.Ordinal)) number = number[4..];
        else if (number.StartsWith('0')) number = number[1..];
        else return false;
        if (number.Length == 10 && number[0] == '3' && number[1] is >= '0' and <= '6') return true;
        foreach (string area in AreaCodes)
        {
            if (!number.StartsWith(area, StringComparison.Ordinal)) continue;
            string subscriber = number[area.Length..];
            if (area is "21" or "42") return subscriber.Length == 8 && subscriber[0] == '3';
            return (area.Length == 2 ? subscriber.Length == 7 :
                number.Length is 9 or 10) &&
                subscriber[0] is >= '2' and <= '9';
        }
        return false;
    }
}
