using System.Globalization;

namespace FlatKharchKhata;

/// <summary>
/// Works out amounts typed as sums, the way the Excel sheet used them: "520+1700", "=24*140".
/// Only numbers, + - * / and brackets are allowed; anything else counts as 0.
/// </summary>
public static class Expr
{
    public static decimal Eval(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var s = new string(text.Trim().TrimStart('=').Where(c => !char.IsWhiteSpace(c) && c != ',').ToArray());
        if (s.Length == 0) return 0;
        try
        {
            var p = new Parser(s);
            var v = p.Expression();
            if (!p.AtEnd || double.IsNaN(v) || double.IsInfinity(v)) return 0;
            return Math.Round((decimal)v, 2);
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            return 0;
        }
    }

    private sealed class Parser(string s)
    {
        private int _i;
        public bool AtEnd => _i == s.Length;
        private char Peek => _i < s.Length ? s[_i] : '\0';

        public double Expression()
        {
            var v = Term();
            while (Peek is '+' or '-')
            {
                var op = s[_i++];
                var t = Term();
                v = op == '+' ? v + t : v - t;
            }
            return v;
        }

        private double Term()
        {
            var v = Factor();
            while (Peek is '*' or '/')
            {
                var op = s[_i++];
                var f = Factor();
                v = op == '*' ? v * f : v / f;
            }
            return v;
        }

        private double Factor()
        {
            if (Peek == '-') { _i++; return -Factor(); }
            if (Peek == '+') { _i++; return Factor(); }
            if (Peek == '(')
            {
                _i++;
                var v = Expression();
                if (Peek != ')') throw new FormatException();
                _i++;
                return v;
            }
            var start = _i;
            while (char.IsAsciiDigit(Peek) || Peek == '.') _i++;
            if (start == _i) throw new FormatException();
            return double.Parse(s.AsSpan(start, _i - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }
    }
}
