using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PackageGuardian.Core.Ignore
{
    /// <summary>
    /// Parse and evaluate .pgignore patterns.
    /// Supports glob-style patterns similar to .gitignore.
    /// </summary>
    public sealed class IgnoreRules
    {
        private readonly List<IgnorePattern> _patterns;

        public IgnoreRules()
        {
            _patterns = new List<IgnorePattern>();
        }

        /// <summary>
        /// Load patterns from .pgignore file.
        /// </summary>
        public static IgnoreRules FromFile(string pgignorePath)
        {
            var rules = new IgnoreRules();
            
            if (!File.Exists(pgignorePath))
                return rules;
            
            foreach (string line in File.ReadAllLines(pgignorePath))
            {
                string trimmed = line.Trim();
                
                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;
                
                rules.AddPattern(trimmed);
            }
            
            return rules;
        }

        /// <summary>
        /// Add a pattern to the rules.
        /// </summary>
        public void AddPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return;
            
            bool negate = pattern.StartsWith("!");
            if (negate)
                pattern = pattern.Substring(1);
            
            _patterns.Add(new IgnorePattern(pattern, negate));
        }

        /// <summary>
        /// Check if a path should be ignored.
        /// </summary>
        public bool IsIgnored(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return true;
            
            // First check default ignores (fast path)
            if (DefaultIgnores.IsIgnoredByDefault(path))
                return true;
            
            // Normalize path
            string normalizedPath = path.Replace('\\', '/');
            
            // Evaluate custom patterns (last match wins)
            bool ignored = false;
            foreach (var pattern in _patterns)
            {
                if (pattern.Matches(normalizedPath))
                {
                    ignored = !pattern.Negate;
                }
            }
            
            return ignored;
        }

        private class IgnorePattern
        {
            public string Pattern { get; }
            public bool Negate { get; }
            private readonly Regex _regex;

            public IgnorePattern(string pattern, bool negate)
            {
                Pattern = pattern;
                Negate = negate;
                _regex = ConvertGlobToRegex(pattern);
            }

            public bool Matches(string path)
            {
                return _regex.IsMatch(path);
            }

            private static Regex ConvertGlobToRegex(string glob)
            {
                // Convert glob pattern to regex
                string pattern = "^";
                
                bool matchDirectory = glob.EndsWith("/");
                if (matchDirectory)
                    glob = glob.TrimEnd('/');
                
                for (int i = 0; i < glob.Length; i++)
                {
                    char c = glob[i];
                    
                    switch (c)
                    {
                        case '*':
                            if (i + 1 < glob.Length && glob[i + 1] == '*')
                            {
                                // ** matches any number of directories
                                if (i + 2 < glob.Length && glob[i + 2] == '/')
                                {
                                    pattern += "(?:.*/)";
                                    i += 2; // Skip **/ 
                                }
                                else
                                {
                                    pattern += ".*";
                                    i++; // Skip second *
                                }
                            }
                            else
                            {
                                // * matches anything except /
                                pattern += "[^/]*";
                            }
                            break;
                        
                        case '?':
                            pattern += "[^/]";
                            break;
                        
                        case '.':
                        case '(':
                        case ')':
                        case '+':
                        case '|':
                        case '^':
                        case '$':
                        case '@':
                        case '%':
                            pattern += "\\" + c;
                            break;
                        
                        case '[':
                            // Character class
                            int closeBracket = glob.IndexOf(']', i);
                            if (closeBracket != -1)
                            {
                                string charClass = glob.Substring(i, closeBracket - i + 1);
                                pattern += charClass;
                                i = closeBracket;
                            }
                            else
                            {
                                pattern += "\\[";
                            }
                            break;
                        
                        default:
                            pattern += c;
                            break;
                    }
                }
                
                if (matchDirectory)
                {
                    pattern += "(?:/|$)";
                }
                else
                {
                    pattern += "$";
                }
                
                return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }
        }
    }
}

