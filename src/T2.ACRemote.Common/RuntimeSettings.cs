using System;
using System.Collections.Generic;
using System.Globalization;

namespace T2.ACRemote.Common
{
    public sealed class RuntimeSettings
    {
        private readonly IDictionary<string, string> _defaults;
        private readonly IDictionary<string, string> _arguments;

        public RuntimeSettings(string[] args, IDictionary<string, string> defaults)
        {
            _defaults = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
            _arguments = ParseArguments(args);
        }

        public string Get(string key)
        {
            string value;
            if (_arguments.TryGetValue(key, out value)) return value;
            value = Environment.GetEnvironmentVariable("T2AC_" + key.ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(value)) return value;
            if (_defaults.TryGetValue(key, out value)) return value;
            throw new InvalidOperationException("Missing setting: " + key);
        }

        public int GetInt(string key)
        {
            int value;
            if (!int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new InvalidOperationException("Setting is not an integer: " + key);
            return value;
        }

        public static bool WantsHelp(string[] args)
        {
            foreach (var arg in args)
                if (arg == "--help" || arg == "-h" || arg == "/?") return true;
            return false;
        }

        private static IDictionary<string, string> ParseArguments(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var arg in args)
            {
                if (!arg.StartsWith("--", StringComparison.Ordinal)) continue;
                var separator = arg.IndexOf('=');
                if (separator <= 2) continue;
                result[arg.Substring(2, separator - 2)] = arg.Substring(separator + 1);
            }
            return result;
        }
    }
}
