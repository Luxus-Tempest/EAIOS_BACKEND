using System.Globalization;
using System.Text;

namespace EAIOS.Api.Infrastructure.Security;

/// <summary>
/// Évalue la condition d'une politique d'accès par attributs.
///
/// <para>
/// La condition est une expression courte, lisible par un administrateur :
/// <c>resource.classification == "StrictlyConfidential" &amp;&amp; !user.hasRole("officier de sécurité")</c>.
/// Elle est évaluée contre un sac d'attributs — la personne, la ressource,
/// l'instant — sans aucun accès au système : pas d'appel, pas d'affectation,
/// pas de boucle. Une condition illisible fait que la politique <b>ne
/// s'applique pas</b>, et l'erreur est journalisée : on ne devine jamais.
/// </para>
/// <para>Grammaire :</para>
/// <code>
///   expr   := or
///   or     := and ( '||' and )*
///   and    := unary ( '&amp;&amp;' unary )*
///   unary  := '!' unary | compare
///   compare:= value ( ( '==' | '!=' | '&gt;' | '&gt;=' | '&lt;' | '&lt;=' | 'in' | 'contains' ) value )?
///   value  := string | number | true | false | null | '[' value, … ']' | '(' expr ')'
///           | ident ( '(' args ')' )?
/// </code>
/// <para>
/// Identifiants : <c>user.id</c>, <c>user.email</c>, <c>user.roles</c>,
/// <c>user.isOrgAdmin</c>, <c>user.departmentIds</c>, <c>user.workspaceIds</c>,
/// <c>resource.classification</c>, <c>resource.type</c>, <c>resource.ownerId</c>,
/// <c>resource.workspaceId</c>, <c>resource.departmentId</c>, <c>resource.tags</c>,
/// <c>resource.hasLegalHold</c>, <c>time.hour</c>, <c>time.weekday</c>, <c>permission</c>.
/// Fonctions : <c>user.hasRole(x)</c>, <c>user.hasPermission(x)</c>,
/// <c>user.inDepartment(x)</c>, <c>user.inWorkspace(x)</c>, <c>resource.hasTag(x)</c>,
/// <c>startsWith(s, p)</c>, <c>endsWith(s, p)</c>.
/// </para>
/// </summary>
public static class PolicyConditionEvaluator
{
    public static bool TryEvaluate(string condition, IReadOnlyDictionary<string, object?> attributes, out bool result, out string? error)
    {
        result = false;
        error  = null;

        if (string.IsNullOrWhiteSpace(condition))
        {
            // Une politique sans condition s'applique toujours.
            result = true;
            return true;
        }

        try
        {
            var parser = new Parser(Tokenizer.Tokenize(condition), attributes);
            var value  = parser.ParseExpression();
            parser.ExpectEnd();
            result = Truthy(value);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool Truthy(object? value) => value switch
    {
        null           => false,
        bool b         => b,
        string s       => s.Length > 0,
        double d       => d != 0,
        List<object?> l => l.Count > 0,
        _              => true,
    };

    // ── Lexique ───────────────────────────────────────────────────────────────

    private enum Kind { Ident, String, Number, Op, End }

    private readonly record struct Token(Kind Kind, string Text);

    private static class Tokenizer
    {
        public static List<Token> Tokenize(string input)
        {
            var tokens = new List<Token>();
            var i = 0;
            while (i < input.Length)
            {
                var c = input[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (c is '"' or '\'')
                {
                    var quote = c;
                    var sb = new StringBuilder();
                    i++;
                    while (i < input.Length && input[i] != quote)
                    {
                        if (input[i] == '\\' && i + 1 < input.Length) { sb.Append(input[i + 1]); i += 2; continue; }
                        sb.Append(input[i++]);
                    }
                    if (i >= input.Length) throw new FormatException("Chaîne non terminée.");
                    i++;
                    tokens.Add(new Token(Kind.String, sb.ToString()));
                    continue;
                }

                if (char.IsDigit(c))
                {
                    var start = i;
                    while (i < input.Length && (char.IsDigit(input[i]) || input[i] == '.')) i++;
                    tokens.Add(new Token(Kind.Number, input[start..i]));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] is '_' or '.')) i++;
                    tokens.Add(new Token(Kind.Ident, input[start..i]));
                    continue;
                }

                string? op = null;
                foreach (var candidate in new[] { "==", "!=", ">=", "<=", "&&", "||", ">", "<", "!", "(", ")", "[", "]", "," })
                {
                    if (string.CompareOrdinal(input, i, candidate, 0, candidate.Length) == 0) { op = candidate; break; }
                }
                if (op is null) throw new FormatException($"Caractère inattendu « {c} » en position {i}.");
                tokens.Add(new Token(Kind.Op, op));
                i += op.Length;
            }
            tokens.Add(new Token(Kind.End, ""));
            return tokens;
        }
    }

    // ── Analyse et évaluation ─────────────────────────────────────────────────

    private sealed class Parser(List<Token> tokens, IReadOnlyDictionary<string, object?> attributes)
    {
        private int _pos;
        private Token Current => tokens[_pos];

        public void ExpectEnd()
        {
            if (Current.Kind != Kind.End) throw new FormatException($"Jeton inattendu « {Current.Text} ».");
        }

        public object? ParseExpression() => ParseOr();

        private object? ParseOr()
        {
            var left = ParseAnd();
            while (Is("||")) { _pos++; var right = ParseAnd(); left = Truthy(left) || Truthy(right); }
            return left;
        }

        private object? ParseAnd()
        {
            var left = ParseUnary();
            while (Is("&&")) { _pos++; var right = ParseUnary(); left = Truthy(left) && Truthy(right); }
            return left;
        }

        private object? ParseUnary()
        {
            if (Is("!")) { _pos++; return !Truthy(ParseUnary()); }
            return ParseCompare();
        }

        private object? ParseCompare()
        {
            var left = ParseValue();

            if (Current.Kind == Kind.Op && Current.Text is "==" or "!=" or ">" or ">=" or "<" or "<=")
            {
                var op = Current.Text; _pos++;
                var right = ParseValue();
                return Compare(op, left, right);
            }

            if (Current.Kind == Kind.Ident && Current.Text is "in" or "contains")
            {
                var op = Current.Text; _pos++;
                var right = ParseValue();
                return op == "in" ? Contains(right, left) : Contains(left, right);
            }

            return left;
        }

        private object? ParseValue()
        {
            var token = Current;
            switch (token.Kind)
            {
                case Kind.String: _pos++; return token.Text;
                case Kind.Number: _pos++; return double.Parse(token.Text, CultureInfo.InvariantCulture);
                case Kind.Op when token.Text == "(":
                {
                    _pos++;
                    var inner = ParseExpression();
                    Expect(")");
                    return inner;
                }
                case Kind.Op when token.Text == "[":
                {
                    _pos++;
                    var list = new List<object?>();
                    if (!Is("]"))
                    {
                        list.Add(ParseExpression());
                        while (Is(",")) { _pos++; list.Add(ParseExpression()); }
                    }
                    Expect("]");
                    return list;
                }
                case Kind.Ident:
                {
                    _pos++;
                    switch (token.Text)
                    {
                        case "true":  return true;
                        case "false": return false;
                        case "null":  return null;
                    }

                    if (Is("("))
                    {
                        _pos++;
                        var args = new List<object?>();
                        if (!Is(")"))
                        {
                            args.Add(ParseExpression());
                            while (Is(",")) { _pos++; args.Add(ParseExpression()); }
                        }
                        Expect(")");
                        return Call(token.Text, args);
                    }

                    return attributes.TryGetValue(token.Text, out var value) ? value : null;
                }
                default:
                    throw new FormatException($"Valeur attendue, trouvé « {token.Text} ».");
            }
        }

        private object? Call(string name, List<object?> args)
        {
            object? Attr(string key) => attributes.TryGetValue(key, out var v) ? v : null;
            static string? Str(object? o) => o?.ToString();

            return name switch
            {
                "user.hasRole"       => Contains(Attr("user.roles"), args.ElementAtOrDefault(0)),
                "user.hasPermission" => Contains(Attr("user.permissions"), args.ElementAtOrDefault(0))
                                        || Contains(Attr("user.permissions"), "*"),
                "user.inDepartment"  => Contains(Attr("user.departmentIds"), args.ElementAtOrDefault(0)),
                "user.inWorkspace"   => Contains(Attr("user.workspaceIds"), args.ElementAtOrDefault(0)),
                "resource.hasTag"    => Contains(Attr("resource.tags"), args.ElementAtOrDefault(0)),
                "startsWith"         => (Str(args.ElementAtOrDefault(0)) ?? "").StartsWith(Str(args.ElementAtOrDefault(1)) ?? "", StringComparison.OrdinalIgnoreCase),
                "endsWith"           => (Str(args.ElementAtOrDefault(0)) ?? "").EndsWith(Str(args.ElementAtOrDefault(1)) ?? "", StringComparison.OrdinalIgnoreCase),
                _                    => throw new InvalidOperationException($"Fonction inconnue « {name} »."),
            };
        }

        private static bool Contains(object? collection, object? item) => collection switch
        {
            List<object?> list => list.Any(v => Equal(v, item)),
            string s           => item is not null && s.Contains(item.ToString() ?? "", StringComparison.OrdinalIgnoreCase),
            _                  => false,
        };

        private static object Compare(string op, object? left, object? right)
        {
            if (op is "==" ) return Equal(left, right);
            if (op is "!=") return !Equal(left, right);

            if (left is double l && right is double r)
                return op switch { ">" => l > r, ">=" => l >= r, "<" => l < r, "<=" => l <= r, _ => false };

            var cmp = string.Compare(left?.ToString(), right?.ToString(), StringComparison.OrdinalIgnoreCase);
            return op switch { ">" => cmp > 0, ">=" => cmp >= 0, "<" => cmp < 0, "<=" => cmp <= 0, _ => false };
        }

        private static bool Equal(object? left, object? right)
        {
            if (left is null || right is null) return left is null && right is null;
            if (left is double l && right is double r) return Math.Abs(l - r) < 1e-9;
            if (left is bool lb && right is bool rb) return lb == rb;
            return string.Equals(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private bool Is(string op) => Current.Kind == Kind.Op && Current.Text == op;

        private void Expect(string op)
        {
            if (!Is(op)) throw new FormatException($"« {op} » attendu, trouvé « {Current.Text} ».");
            _pos++;
        }
    }
}
