using System;

namespace SimdUnicode {
    public static class UTF8 {
        public static bool IsUTF8(this char c) => c < 128;
        public static bool IsUTF8(this string s) {
            foreach (var c in s) {
                if (!c.IsUTF8()) return false;
            }
            return true;
        }
        public static bool IsUTF8(this ReadOnlySpan<char> s) {
            foreach (var c in s) {
                if (!c.IsUTF8()) return false;
            }
            return true;
        }
        public static bool IsUTF8(this Span<char> s) {
            foreach (var c in s) {
                if (!c.IsUTF8()) return false;
            }
            return true;
        }
        public static bool IsUTF8(this ReadOnlyMemory<char> s) => IsUTF8(s.Span);
        public static bool IsUTF8(this Memory<char> s) => IsUTF8(s.Span);
    }
}