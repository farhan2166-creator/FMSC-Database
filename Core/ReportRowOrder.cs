using System.Globalization;

namespace FMSC_SMS.Core;

// Presentation only. Order the complete result before pagination, printing or export.
internal static class ReportRowOrder
{
    internal enum ValueKind { Standard, Reference, Month }
    internal sealed record Choice(string Label, string Column = "", bool Descending = false,
        ValueKind Kind = ValueKind.Standard)
    {
        public override string ToString() => Label;
    }

    internal static IReadOnlyList<Choice> Choices(string report, string view, string grouping = "")
    {
        var choices = new List<Choice> { new("Default") };
        void Text(string label, string column, ValueKind kind = ValueKind.Standard)
        {
            choices.Add(new(label + (kind == ValueKind.Reference ? ": ascending" : " (A–Z)"), column, Kind: kind));
            choices.Add(new(label + (kind == ValueKind.Reference ? ": descending" : " (Z–A)"), column, true, kind));
        }
        void Amount(string label, string column)
        {
            choices.Add(new(label + ": highest first", column, true));
            choices.Add(new(label + ": lowest first", column));
        }
        void Date(string label, string column, ValueKind kind = ValueKind.Standard)
        {
            choices.Add(new(label + ": newest first", column, true, kind));
            choices.Add(new(label + ": oldest first", column, Kind: kind));
        }
        void Student(string admissionColumn)
        {
            Text("Student name", "Student");
            Text("Admission number", admissionColumn, ValueKind.Reference);
        }
        switch (report)
        {
            case "GetCollectionsAndReceiptsReport":
                if (view == "Summary") Text("Group", "Group");
                else
                {
                    Date("Payment date", "Date");
                    Text("Receipt / reference", "Receipt / Reference", ValueKind.Reference);
                    Text("Student / family", view == "Receipts" ? "Student / Family" : "Student");
                    Text("Class / section", "Class / Section", ValueKind.Reference);
                }
                Amount(view == "Detailed Transactions" ? "Collection amount" : "Money received",
                    view == "Detailed Transactions" ? "Collection Amount" : "Money Received");
                if (view != "Detailed Transactions") Amount("Adjusted collection", "Adjusted Collection");
                break;
            case "GetOutstandingAndAgingReport":
                if (view is "Students" or "Charges")
                {
                    Student("Adm. / QR No.");
                    Text("Class / section", "Class / Section", ValueKind.Reference);
                }
                if (view == "Families") Text("Family", "Family", ValueKind.Reference);
                Text("Campus", "Campus");
                Amount("Outstanding", "Outstanding");
                if (view != "Aging")
                {
                    Amount("Days overdue", "Days Overdue");
                    Date("Due date", view == "Charges" ? "Due Date" : "Oldest Due Date");
                }
                break;
            case "CreditBalanceRegister":
                if (view == "Students")
                {
                    Student("Adm. / QR No.");
                    Text("Class / section", "Class / Section", ValueKind.Reference);
                }
                else Text("Family / student", "Family / Student", ValueKind.Reference);
                Text("Campus", "Campus");
                Amount("Available credit", "Available Credit");
                Date("Last activity", "Last Activity");
                break;
            case "GetBillingAndDiscountsReport":
                if (view == "Billing Summary")
                {
                    string column = grouping switch { "Day" => "Charge Date", "Week" => "Charge Week Starting", "Month" => "Charge Month", _ => grouping };
                    Text("Group", column);
                    Amount("Net billed", "Net Billed");
                    Amount("Discounts", "Discounts");
                }
                else if (view == "Concession Summary")
                {
                    Text("Campus", "Campus"); Text("Class", "Class", ValueKind.Reference);
                    Text("Fee type", "Fee Type"); Text("Concession", "Concession");
                    Amount("Students", "Students");
                }
                else
                {
                    Student("Admission Number");
                    Text("Class", "Class", ValueKind.Reference); Text("Fee type", "Fee Type");
                    if (view == "Concession Assignments")
                    {
                        Amount("Assigned discount", "Discount"); Date("Effective from", "Effective From");
                    }
                    else
                    {
                        Date("Charge date", "Charge Date"); Amount("Net charge", "Net Charge");
                        Amount("Discount", view == "Discount Detail" ? "Applied Discount" : "Discount");
                    }
                }
                break;
            case "MonthlyFeeReconciliation":
                Date("Billing month", "Billing Month", ValueKind.Month); Text("Group", "Group");
                Amount("Outstanding", "Outstanding"); Amount("Net fees", "Net Fees");
                Amount("Money allocated", "Money Allocated");
                break;
            case "CertificateIssueRegister":
                Date("Recorded on", "Recorded On"); Date("Certificate date", "Certificate Date");
                Text("Recipient", "Recipient"); Text("Certificate type", "Certificate Type");
                Text("Certificate number", "Certificate No.", ValueKind.Reference);
                Text("Student / staff number", "Student / Staff No.", ValueKind.Reference);
                break;
        }
        return choices;
    }

    internal static DataTable Apply(DataTable source, Choice choice)
    {
        if (choice.Column.Length == 0) return source;
        if (!source.Columns.Contains(choice.Column))
            throw new InvalidOperationException("The selected order is unavailable for this report view. Choose Default and run the report again.");
        int CompareRows(int left, int right)
        {
            object a = source.Rows[left][choice.Column], b = source.Rows[right][choice.Column];
            // Missing values stay last in either direction.
            if (a is DBNull || b is DBNull) return a is DBNull ? (b is DBNull ? 0 : 1) : -1;
            int comparison;
            if (choice.Kind == ValueKind.Reference) comparison = CompareReferences(Convert.ToString(a)!, Convert.ToString(b)!);
            else if (choice.Kind == ValueKind.Month)
                comparison = DateTime.ParseExact(Convert.ToString(a)!, "MMM yyyy", CultureInfo.CurrentCulture)
                    .CompareTo(DateTime.ParseExact(Convert.ToString(b)!, "MMM yyyy", CultureInfo.CurrentCulture));
            else comparison = a is string text ? StringComparer.CurrentCultureIgnoreCase.Compare(text, Convert.ToString(b))
                : ((IComparable)a).CompareTo(b);
            return choice.Descending ? -Math.Sign(comparison) : Math.Sign(comparison);
        }
        IEnumerable<int> Sort(IEnumerable<int> indices) => indices.OrderBy(i => i, Comparer<int>.Create(CompareRows));
        var layout = CollectionReceiptGrouping.Layout(source);
        var order = new List<int>();
        if (layout is null)
        {
            var indices = Enumerable.Range(0, source.Rows.Count).ToArray();
            order.AddRange(Sort(indices.Where(i => !ReportPrintSummary.IsTotalRow(source.Rows[i]))));
            order.AddRange(indices.Where(i => ReportPrintSummary.IsTotalRow(source.Rows[i])));
        }
        else
        {
            for (int i = 0; i < layout.Length;)
            {
                if (layout[i].Kind != "Detail") { order.Add(i++); continue; }
                int start = i;
                string group = layout[i].GroupKey;
                while (i < layout.Length && layout[i].Kind == "Detail" && layout[i].GroupKey == group) i++;
                order.AddRange(Sort(Enumerable.Range(start, i - start)));
            }
        }
        var result = source.Clone();
        foreach (int i in order) result.ImportRow(source.Rows[i]);
        if (layout is not null) result.ExtendedProperties[CollectionReceiptGrouping.LayoutKey] = order.Select(i => layout[i]).ToArray();
        return result;
    }

    // Official numbers and QR/certificate references compare digit runs numerically.
    private static int CompareReferences(string left, string right)
    {
        int a = 0, b = 0;
        while (a < left.Length && b < right.Length)
        {
            if (char.IsAsciiDigit(left[a]) && char.IsAsciiDigit(right[b]))
            {
                int endA = a, endB = b;
                while (endA < left.Length && char.IsAsciiDigit(left[endA])) endA++;
                while (endB < right.Length && char.IsAsciiDigit(right[endB])) endB++;
                while (a < endA - 1 && left[a] == '0') a++;
                while (b < endB - 1 && right[b] == '0') b++;
                int size = (endA - a).CompareTo(endB - b);
                if (size != 0) return size;
                int digits = left.AsSpan(a, endA - a).SequenceCompareTo(right.AsSpan(b, endB - b));
                if (digits != 0) return digits;
                a = endA; b = endB;
            }
            else
            {
                int text = char.ToUpperInvariant(left[a++]).CompareTo(char.ToUpperInvariant(right[b++]));
                if (text != 0) return text;
            }
        }
        return (left.Length - a).CompareTo(right.Length - b);
    }
}
