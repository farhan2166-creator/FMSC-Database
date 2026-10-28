# Parse the reviewed schema-only baseline and migration; compare all business
# writes, procedure calls, errors, transaction statements and return statements.
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dll=Join-Path $env:USERPROFILE '.nuget/packages/microsoft.sqlpackage/170.5.96/tools/net8.0/any/Microsoft.SqlServer.TransactSql.ScriptDom.dll'
Add-Type -Path $dll
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -ReferencedAssemblies @($dll,'System.Linq.dll','System.Collections.dll','System.Runtime.dll','System.Text.RegularExpressions.dll') -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
public sealed class AuditBusinessVisitor : TSqlFragmentVisitor {
 public List<string> Statements = new List<string>();
 void Keep(TSqlFragment node) {
  var text=string.Join(" ",node.ScriptTokenStream.Skip(node.FirstTokenIndex).Take(node.LastTokenIndex-node.FirstTokenIndex+1)
   .Where(t=>t.TokenType!=TSqlTokenType.WhiteSpace && t.TokenType!=TSqlTokenType.SingleLineComment && t.TokenType!=TSqlTokenType.MultilineComment)
   .Select(t=>t.Text.Replace("[","").Replace("]","").ToUpperInvariant()));
  if(Regex.IsMatch(text,@"\b(AUDITLOGS|AUDITOPERATIONS|WRITEAUDITLOG)\b|@OLDUSERROLES\b|@OLDPERMISSIONIDS\b"))return;
  if(text.Contains("GETLIFECYCLEREPAIRSNAPSHOT") && text.Contains("@AFTER"))return;
  Statements.Add(text);
 }
 public override void ExplicitVisit(InsertStatement n){Keep(n);}
 public override void ExplicitVisit(UpdateStatement n){Keep(n);}
 public override void ExplicitVisit(DeleteStatement n){Keep(n);}
 public override void ExplicitVisit(MergeStatement n){Keep(n);}
 public override void ExplicitVisit(ExecuteStatement n){Keep(n);}
 public override void ExplicitVisit(ThrowStatement n){Keep(n);}
 public override void ExplicitVisit(BeginTransactionStatement n){Keep(n);}
 public override void ExplicitVisit(CommitTransactionStatement n){Keep(n);}
 public override void ExplicitVisit(RollbackTransactionStatement n){Keep(n);}
 public override void ExplicitVisit(ReturnStatement n){Keep(n);}
 public static string Capture(string sql) {
  IList<ParseError> errors; var tree=new TSql160Parser(true).Parse(new StringReader(sql),out errors);
  if(errors.Count>0)throw new Exception(string.Join("; ",errors.Select(e=>e.Line+": "+e.Message)));
  var visitor=new AuditBusinessVisitor();tree.Accept(visitor);return string.Join("\n",visitor.Statements);
 }
}
'@
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $root 'fmsc.dacpac'))
try{$r=[IO.StreamReader]::new($zip.GetEntry('model.xml').Open());try{[xml]$model=$r.ReadToEnd()}finally{$r.Dispose()}}finally{$zip.Dispose()}
$ns=[Xml.XmlNamespaceManager]::new($model.NameTable);$ns.AddNamespace('d',$model.DocumentElement.NamespaceURI)
$sql=[IO.File]::ReadAllText((Join-Path $root 'Migrations/59.sql'))
$count=0
foreach($batch in [regex]::Split($sql,'(?im)^\s*GO\s*\r?$')){
 $match=[regex]::Match($batch,'(?im)^CREATE OR ALTER PROCEDURE\s+([\w.\[\]]+)')
 if(-not $match.Success){continue}
 $name=$match.Groups[1].Value.Replace('[','').Replace(']','');$schema,$proc=$name.Split('.')
 $baseline=$model.SelectSingleNode("//d:Element[@Type='SqlProcedure' and @Name='[$schema].[$proc]']/d:Property[@Name='BodyScript']/d:Value",$ns)
 if($null -eq $baseline){throw "Missing reviewed baseline $name"}
 $before=[AuditBusinessVisitor]::Capture($baseline.InnerText)
 $after=[AuditBusinessVisitor]::Capture($batch)
 if($before -ne $after){$before | Set-Content (Join-Path $root 'artifacts/business-before.txt');$after | Set-Content (Join-Path $root 'artifacts/business-after.txt');throw "Business SQL changed in $name"}
 $count++;Write-Output "PASS: $name business writes, calls, errors and transactions unchanged."
}
if($count -ne 19){throw "Expected 19 procedures; found $count"}
Write-Output "PASS: $count parsed business-SQL equivalence checks."
