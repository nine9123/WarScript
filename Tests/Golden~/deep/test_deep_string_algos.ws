# String algorithm tests: counting, searching, building

# ── 1. Count character occurrences ──
fun count_char [s, ch, len]
    count = 0
    loop i in 0..len
        if s{i} == ch
            count += 1
        end
    end
    return count
end

assert count_char ["hello", "l", 5] == 2
assert count_char ["hello", "z", 5] == 0
assert count_char ["aaa", "a", 3] == 3

# ── 2. Find first occurrence ──
fun find_char [s, ch, len]
    loop i in 0..len
        if s{i} == ch
            return i
        end
    end
    return -1
end

assert find_char ["hello", "l", 5] == 2
assert find_char ["hello", "o", 5] == 4
assert find_char ["hello", "h", 5] == 0
assert find_char ["hello", "z", 5] == -1

# ── 3. String contains check ──
fun str_contains [haystack, needle, h_len, n_len]
    if n_len > h_len
        return false
    end
    loop i in 0..h_len - n_len + 1
        match = true
        loop j in 0..n_len
            if haystack{i + j} != needle{j}
                match = false
                break
            end
        end
        if match
            return true
        end
    end
    return false
end

assert str_contains ["hello world", "world", 11, 5]
assert str_contains ["hello world", "hello", 11, 5]
assert str_contains ["hello world", "lo wo", 11, 5]
not_found = str_contains ["hello world", "xyz", 11, 3]
assert !not_found
assert str_contains ["aaa", "a", 3, 1]

# ── 4. Build padded number ──
fun pad_left [n, width]
    s = "" + n
    # count length via index (strings aren't iterable)
    len = 0
    loop s{len} != null
        len += 1
    end
    loop len < width
        s = "0" + s
        len += 1
    end
    return s
end

assert pad_left [5, 3] == "005"
assert pad_left [42, 3] == "042"
assert pad_left [123, 3] == "123"
assert pad_left [1, 1] == "1"
assert pad_left [0, 4] == "0000"

# ── 5. Join array of strings ──
fun join [arr, n, sep]
    result = ""
    loop i in 0..n
        if i > 0
            result += sep
        end
        result += arr{i}
    end
    return result
end

assert join [{"a", "b", "c"}, 3, ","] == "a,b,c"
assert join [{"hello", "world"}, 2, " "] == "hello world"
assert join [{"x"}, 1, ","] == "x"
assert join [{"1", "2", "3", "4"}, 4, "-"] == "1-2-3-4"

# ── 6. String starts with ──
fun starts_with [s, prefix, s_len, p_len]
    if p_len > s_len
        return false
    end
    loop i in 0..p_len
        if s{i} != prefix{i}
            return false
        end
    end
    return true
end

assert starts_with ["hello", "hel", 5, 3]
assert starts_with ["hello", "hello", 5, 5]
sw = starts_with ["hello", "world", 5, 5]
assert !sw

# ── 7. String ends with ──
fun ends_with [s, suffix, s_len, su_len]
    if su_len > s_len
        return false
    end
    offset = s_len - su_len
    loop i in 0..su_len
        if s{offset + i} != suffix{i}
            return false
        end
    end
    return true
end

assert ends_with ["hello", "llo", 5, 3]
assert ends_with ["hello", "hello", 5, 5]
ew = ends_with ["hello", "hel", 5, 3]
assert !ew

# ── 8. Capitalize first letter (simple) ──
fun str_length [s]
    n = 0
    # strings aren't iterable; probe index until null
    loop s{n} != null
        n += 1
    end
    return n
end

assert str_length ["hello"] == 5
assert str_length [""] == 0
assert str_length ["a"] == 1
assert str_length ["test string"] == 11

# ── 9. Repeat string N times ──
fun repeat [s, n]
    result = ""
    loop i in 0..n
        result += s
    end
    return result
end

assert repeat ["ab", 3] == "ababab"
assert repeat ["x", 0] == ""
assert repeat ["-", 5] == "-----"

# ── 10. Build CSV line ──
fun csv_line [fields, n]
    result = ""
    loop i in 0..n
        if i > 0
            result += ","
        end
        result += fields{i}
    end
    return result
end

assert csv_line [{"name", "age", "email"}, 3] == "name,age,email"
assert csv_line [{"a"}, 1] == "a"
assert csv_line [{"1", "2", "3", "4", "5"}, 5] == "1,2,3,4,5"
