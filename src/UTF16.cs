using System;

namespace SimdUnicode {
    public static class UTF16 {
        public static bool IsUTF16(this char c) => c < 128;
        public static bool IsUTF16(this string s) {
            foreach (var c in s) {
                if (!c.IsUTF16()) return false;
            }
            return true;
        }
        public static bool IsUTF16(this ReadOnlySpan<char> s) {
            foreach (var c in s) {
                if (!c.IsUTF16()) return false;
            }
            return true;
        }
        public static bool IsUTF16(this Span<char> s) {
            foreach (var c in s) {
                if (!c.IsUTF16()) return false;
            }
            return true;
        }
        public static bool IsUTF16(this ReadOnlyMemory<char> s) => IsUTF16(s.Span);
        public static bool IsUTF16(this Memory<char> s) => IsUTF16(s.Span);
    }
}