using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace scalars
{
    public enum ScalarName
    {
        UtcNow, // no arguments
        Sin, // single argument type
        ToDouble, // multiple argument types
        Stracat, // variable arguments
        Strjoin, // fixed arguments followed by variable arguments 
        Coaelesce // muntiple arguments, multiple argument types
    }

    public class ScalarAttribute : Attribute
    {
        public ScalarName Name { get; set; }
    }

    public class KnownFunctions
    {

        [Scalar(Name = ScalarName.Sin)]
        static double? Sin(double? x) => x.HasValue? Math.Sin(x.Value): (double?) null;
        [Scalar(Name = ScalarName.ToDouble)] static double? ToDouble(long? x) => x.HasValue ? Convert.ToDouble(x.Value) : (double?) null;
        [Scalar(Name = ScalarName.ToDouble)] static double? ToDouble(string s) => s != null ? Double.Parse(s) : (double?) null;
        [Scalar(Name = ScalarName.Stracat)] static string Stracat(IEnumerable<string> els) => String.Concat(els);
        [Scalar(Name = ScalarName.Coaelesce)] static long? CoalesceLongs(IEnumerable<long?> els) => els.Where(x => x.HasValue).FirstOrDefault();
        [Scalar(Name = ScalarName.Coaelesce)] static Func<IEnumerable<string>, string> coalesceStrings = new Func<IEnumerable<string>, string>(els => els.First(e => e != null));
        [Scalar(Name = ScalarName.Coaelesce)] static string CoalesceStrings(IEnumerable<string> els) => els.Where(x => x != null).FirstOrDefault();
        [Scalar(Name = ScalarName.Coaelesce)] static double? CoalesceDoubles(IEnumerable<double?> els) => els.Where(x => x.HasValue).FirstOrDefault();
        [Scalar(Name = ScalarName.UtcNow)] static DateTime? UtcNow() => DateTime.UtcNow;
        [Scalar(Name = ScalarName.Strjoin)] static string Strjoin(string separator, IEnumerable<String> els) => string.Join(separator, els);
    }

    public class Matcher
    {
        public static Dictionary<Type, char> _typeCodes = new Dictionary<Type, char>
        { { typeof(long?), 'l' },
            { typeof(IEnumerable<long?>), 'L' },
            { typeof(double?), 'f' },
            { typeof(IEnumerable<double?>), 'F' },
            { typeof(bool?), 'b' },
            { typeof(IEnumerable<bool?>), 'B' },
            { typeof(DateTime?), 'd' },
            { typeof(IEnumerable<DateTime?>), 'D' },
            { typeof(TimeSpan?), 't' },
            { typeof(IEnumerable<TimeSpan?>), 'T' },
            { typeof(string), 's' },
            { typeof(IEnumerable<string>), 'S' },
        };
        public static Dictionary<Type, char> _objectCodes = new Dictionary<Type, char>
        { { typeof(long), 'l' },
            { typeof(double), 'f' },
            { typeof(bool), 'b' },
            { typeof(DateTime), 'd' },
            { typeof(TimeSpan), 't' },
            { typeof(string), 's' },
            { typeof(IEnumerable<string>), 'S' },
        };

        Dictionary<string, MethodInfo> _methods = new Dictionary<string, MethodInfo>();

        // TODO check template for ambiguity
        public void RegisterTemplate(string template, MethodInfo methodInfo)
        {
            _methods[template] = methodInfo;
        }

        public MethodInfo MethodByTemplate(string template)
        {
            return _methods[template];
        }

        public static bool SignatureMatchesTemplate(string signature, string template)
        {
            int si = 0;
            foreach (char tc in template)
            {
                if (char.IsLower(tc))
                {
                    if (tc != signature[si])
                    {
                        return false;
                    }
                    si++;
                }
                else
                {
                    char cc = char.ToLower(tc);
                    int consumed = 0;
                    while (si < signature.Length && signature[si] == cc)
                    {
                        si++;
                        consumed++;
                    }
                    if (consumed == 0)
                    {
                        return false;
                    }
                }
            }
            return si == signature.Length;

        }

        public static string GetTemplate(MethodInfo methodInfo)
        {
            return string.Concat(methodInfo.GetParameters().Select(x => _typeCodes[x.ParameterType]));
        }
        public static string GetSignature(object[] args)
        {
            return string.Concat(args.Select(x => _objectCodes[x.GetType()]));
        }

        public string MatchSignatureToTemplate(string signature)
        {
            string candidate = null;
            foreach (var template in _methods.Keys)
            {
                if (SignatureMatchesTemplate(signature, template))
                {
                    Debug.Assert(candidate == null);
                    candidate = template;
                }
            }
            return candidate;
        }

        public static object SanitizeArgument(object arg)
        {
            if (arg.GetType() == typeof(long))
            {
                return (long?) arg;
            }
            else if (arg.GetType() == typeof(double))
            {
                return (double?) arg;
            }
            else if (arg.GetType() == typeof(bool))
            {
                return (bool?) arg;
            }
            else if (arg.GetType() == typeof(DateTime))
            {
                return (DateTime?) arg;
            }
            else if (arg.GetType() == typeof(TimeSpan))
            {
                return (TimeSpan?) arg;
            }
            else
            {
                Debug.Assert(arg.GetType() == typeof(string));
                return arg;
            }
        }

        public static object SanitizeList(List<object> ls)
        {
            Debug.Assert(ls.Count > 0);
            var t = ls[0].GetType();
            if (t == typeof(long))
            {
                return ls.Select(x => (long?) x);
            }
            else if (t == typeof(double))
            {
                return ls.Select(x => (double?) x);
            }
            else if (t == typeof(bool))
            {
                return ls.Select(x => (bool?) x);
            }
            else if (t == typeof(DateTime))
            {
                return ls.Select(x => (DateTime?) x);
            }
            else if (t == typeof(TimeSpan))
            {
                return ls.Select(x => (TimeSpan?) x);
            }
            else
            {
                Debug.Assert(t == typeof(string));
                return ls.Select(x => (string) x).ToList();
            }
        }

        public IEnumerable<object> Compress(string template, object[] arguments)
        {
            var signature = GetSignature(arguments);
            Debug.Assert(arguments.Length == signature.Length);
            int si = 0;
            foreach (var tc in template)
            {
                if (char.IsLower(tc))
                {
                    Debug.Assert(tc == signature[si]);
                    yield return SanitizeArgument(arguments[si]);
                    si++;
                }
                else
                {
                    char cc = char.ToLower(tc);
                    var ls = new List<object>();
                    while (si < signature.Length && signature[si] == cc)
                    {
                        ls.Add(arguments[si]);
                        si++;
                    }
                    Debug.Assert(ls.Count > 0);
                    yield return SanitizeList(ls);
                }
            }

        }
    }

    public static class ScalarExecutor
    {

        public static Dictionary<ScalarName, Matcher> _scalars = new Dictionary<ScalarName, Matcher>();

        static ScalarExecutor()
        {
            foreach (var info in typeof(KnownFunctions).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Where(x => Attribute.IsDefined(x, typeof(ScalarAttribute))))
            {
                var attr = info.GetCustomAttribute(typeof(ScalarAttribute)) as ScalarAttribute;
                var template = Matcher.GetTemplate(info);
                if (!_scalars.ContainsKey(attr.Name))
                {
                    _scalars[attr.Name] = new Matcher();
                }
                _scalars[attr.Name].RegisterTemplate(template, info);
            }

        }

        public static object Execute(ScalarName name, object[] arguments)
        {
            Debug.Assert(_scalars.ContainsKey(name));
            var matcher = _scalars[name];
            var signature = Matcher.GetSignature(arguments);
            var template = matcher.MatchSignatureToTemplate(signature);

            var parameters = matcher.Compress(template, arguments).ToArray();
            var method = matcher.MethodByTemplate(template);
            return method.Invoke(null, parameters);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            {
                var name = ScalarName.Stracat;
                var arguments = new object[] { "foo", "bar" };
                Console.WriteLine(ScalarExecutor.Execute(name, arguments));
            }

            {
                var name = ScalarName.UtcNow;
                var arguments = new object[0];
                Console.WriteLine(ScalarExecutor.Execute(name, arguments));
            }
            {
                var name = ScalarName.Strjoin;
                var arguments = new object[] { ",", "foo", "bar" };
                Console.WriteLine(ScalarExecutor.Execute(name, arguments));
            }
            {
                var name = ScalarName.ToDouble;
                var arguments = new object[] { 42L };
                Console.WriteLine(ScalarExecutor.Execute(name, arguments));
            }
            {
                var name = ScalarName.ToDouble;
                var arguments = new object[] { "42" };
                Console.WriteLine(ScalarExecutor.Execute(name, arguments));
            }
            {
                var name = ScalarName.Coaelesce;
                var arguments = new object[] { "first", "second" };
                Console.WriteLine(ScalarExecutor.Execute(name, arguments));
            }
            {
                var name = ScalarName.Coaelesce;
                var arguments = new object[] { 1L, 2L };
                Console.WriteLine(ScalarExecutor.Execute(name, arguments));
            }
            {
                var name = ScalarName.Coaelesce;
                var arguments = new object[] { 1.0, 2.0 };
                Console.WriteLine(ScalarExecutor.Execute(name, arguments));
            }
        }
    }
}