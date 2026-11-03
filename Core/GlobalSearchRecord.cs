namespace FMSC_SMS.Core;

internal enum GlobalSearchScope { All, Students, Bills, Receipts }

internal sealed record GlobalSearchRecord(string Kind,string Key,int AdmissionId,string Reference,string Name,string Context,int Rank)
{
    internal string Heading=>$"{(Kind=="Student"?"Student":Kind=="Bill"?"Bill":Kind=="Receipt"?"Receipt":"Payment reference")} · {Reference} · {Name}";
    public override string ToString()=>Heading;
}

internal static class GlobalSearchInput
{
    internal const int MaximumLength=120;
    internal static string Normalize(string? text)=>string.Join(' ',Words(text??""));
    internal static bool CanSearch(string text)=>text.Length<=MaximumLength&&(text.Length>=2||text.Length==1&&char.IsDigit(text[0]));
    internal static string EscapeLike(string text)=>text.Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[");
    internal static string[] Words(string text)=>text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
}
