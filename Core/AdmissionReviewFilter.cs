namespace FMSC_SMS.Core;

internal static class AdmissionReviewFilter
{
    internal static string Create(bool pending, bool activeOnly, bool needsReviewOnly, string? search)
    {
        var predicates=new List<string>();
        if(!pending && activeOnly)predicates.Add("[Status] = 'Active'");
        if(!pending && needsReviewOnly)predicates.Add("[Verification] <> 'Verified'");
        string term=(search??"").Trim();
        if(term.Length>0){
            // DataView LIKE has its own wildcard grammar; user input is literal.
            string literal=string.Concat(term.Select(c=>c switch {
                '\''=>"''", '['=>"[[]", ']'=>"[]]", '*'=>"[*]", '%'=>"[%]", _=>c.ToString()
            }));
            predicates.Add("("+string.Join(" OR ",new[]{"Reference","StudentName","FatherName","ContactNumber"}
                .Select(column=>$"ISNULL([{column}], '') LIKE '%{literal}%'"))+")");
        }
        return string.Join(" AND ",predicates);
    }
}
