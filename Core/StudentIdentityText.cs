namespace FMSC_SMS.Core;

// Plain text shared by report projections. Callers supply their display-cased names.
internal static class StudentIdentityText
{
    internal sealed record Member(string Reference,string Name,string Father,string Gender);
    private static string Name(string reference,string name)=>string.Join(" - ",new[]{reference.Trim(),name.Trim()}.Where(value=>value.Length>0));
    private static string Relation(string gender)=>gender.Trim().ToUpperInvariant() switch {
        "FEMALE" or "F" or "GIRL"=>"d/o", "MALE" or "M" or "BOY"=>"s/o", _=>"child of"
    };
    internal static string Format(string reference,string name,string father,string gender)
    {
        string identity=Name(reference,name);father=father.Trim();
        if(father.Length==0)return identity;
        if(string.IsNullOrWhiteSpace(name))return string.Join("\n",new[]{identity,"Father: "+father}.Where(value=>value.Length>0));
        return $"{identity} {Relation(gender)} {father}";
    }
    internal static string Family(IEnumerable<Member> source)
    {
        var members=source.ToArray();
        if(members.Length==0)return "";
        if(members.Length==1)return Format(members[0].Reference,members[0].Name,members[0].Father,members[0].Gender);
        string[] parents=members.Select(member=>member.Father.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(parents.Length!=1)return string.Join(", ",members.Select(member=>Format(member.Reference,member.Name,member.Father,member.Gender)));
        string names=string.Join(", ",members.Select(member=>Name(member.Reference,member.Name)));
        if(parents[0].Length==0)return names;
        string[] relations=members.Select(member=>Relation(member.Gender)).Distinct().ToArray();
        string relation=relations.Length==1 && relations[0]!="child of"?relations[0]:"s/d/o";
        return $"{names} {relation} {parents[0]}";
    }
}
