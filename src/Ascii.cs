using System;

namespace SimdUnicode {
    public static class Ascii {
        public static bool IsAscii(this char c) => c < 128;
        public static bool IsAscii(this string s) {
            foreach (var c in s) {
                if (!c.IsAscii()) return false;
            }
            return true;
        }
        public static bool IsAscii(this ReadOnlySpan<char> s) {
            foreach (var c in s) {
                if (!c.IsAscii()) return false;
            }
            return true;
        }
        public static bool IsAscii(this Span<char> s) {
            foreach (var c in s) {
                if (!c.IsAscii()) return false;
            }
            return true;
        }
        public static bool IsAscii(this ReadOnlyMemory<char> s) => IsAscii(s.Span);
        public static bool IsAscii(this Memory<char> s) => IsAscii(s.Span);
    }
}