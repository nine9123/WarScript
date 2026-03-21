# test_deep_algo_strings.ws — RLE encoding + palindrome check

fun rle_encode [s, len]
    if len == 0
        return ""
    end
    result = ""
    count = 1
    loop i in 1..len
        if s{i} == s{i - 1}
            count += 1
        else
            result += s{i - 1} + count
            count = 1
        end
    end
    result += s{len - 1} + count
    return result
end

assert rle_encode ["aabbbcccc", 9] == "a2b3c4"
assert rle_encode ["abc", 3] == "a1b1c1"
assert rle_encode ["aaaa", 4] == "a4"
assert rle_encode ["a", 1] == "a1"

fun is_palindrome [s, len]
    loop i in 0..len / 2
        if s{i} != s{len - 1 - i}
            return false
        end
    end
    return true
end

assert is_palindrome ["racecar", 7]
assert is_palindrome ["abba", 4]
assert is_palindrome ["a", 1]
assert !is_palindrome ["hello", 5]
assert !is_palindrome ["ab", 2]
assert is_palindrome ["abcba", 5]
