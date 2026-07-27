namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class PackageProtocolIdentifier
    {
        internal static bool IsSafe(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length > 128 ||
                !IsAsciiLetterOrDigit(value[0]))
            {
                return false;
            }

            foreach (char character in value)
            {
                if (!IsAsciiLetterOrDigit(character) &&
                    character != '.' &&
                    character != '_' &&
                    character != '-')
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsAsciiLetterOrDigit(char value)
        {
            return value >= '0' && value <= '9' ||
                value >= 'A' && value <= 'Z' ||
                value >= 'a' && value <= 'z';
        }
    }
}
