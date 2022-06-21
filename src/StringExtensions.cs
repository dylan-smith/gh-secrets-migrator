using System.Text;

namespace SecretsMigrator
{
    public static class StringExtensions
    {
        public static StringContent ToStringContent(this string s) => new(s, Encoding.UTF8, "application/json");
    }
}
